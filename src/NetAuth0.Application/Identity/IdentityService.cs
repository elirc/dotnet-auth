using Microsoft.EntityFrameworkCore;
using NetAuth0.Application.Abstractions;
using NetAuth0.Application.Audit;
using NetAuth0.Application.OAuth;
using NetAuth0.Domain.Identity;

namespace NetAuth0.Application.Identity;

public interface IIdentityService
{
    Task<Guid> SignupAsync(SignupRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<LoginResponse> LoginAsync(LoginRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<MfaEnrollmentResponse> BeginTotpEnrollmentAsync(Guid userId, CancellationToken cancellationToken);
    Task VerifyTotpEnrollmentAsync(VerifyMfaRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<SessionDto>> GetSessionsAsync(Guid userId, CancellationToken cancellationToken);
    Task RevokeSessionFamilyAsync(Guid userId, Guid familyId, RequestContext context, CancellationToken cancellationToken);
}

public sealed class IdentityService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    ISecretGenerator secrets,
    ITokenIssuer tokenIssuer,
    ITotpService totp,
    IClock clock,
    IAuditWriter audit) : IIdentityService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

    public async Task<Guid> SignupAsync(SignupRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Slug == request.TenantSlug && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Tenant not found.");

        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.TenantId == tenant.Id && x.Email == email, cancellationToken))
        {
            throw new InvalidOperationException("Email is already registered.");
        }

        var user = new User(tenant.Id, email, request.DisplayName, passwordHasher.Hash(request.Password));
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(tenant.Id, "user.signup", user.Id, null, context, new { user.Email }, cancellationToken);
        return user.Id;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Slug == request.TenantSlug && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Invalid credentials.");

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.Email == email, cancellationToken)
            ?? throw new InvalidOperationException("Invalid credentials.");

        if (user.IsBlocked || user.LockoutEndsAt > clock.UtcNow)
        {
            await audit.WriteAsync(tenant.Id, "login.blocked", user.Id, null, context, null, cancellationToken);
            throw new InvalidOperationException("Account is temporarily locked.");
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            user.RegisterFailure(clock.UtcNow, MaxFailedAttempts, LockoutWindow);
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(tenant.Id, "login.failure", user.Id, null, context, new { reason = "bad_password" }, cancellationToken);
            throw new InvalidOperationException("Invalid credentials.");
        }

        var enrollment = await db.MfaEnrollments.SingleOrDefaultAsync(x => x.UserId == user.Id && x.VerifiedAt != null && x.RevokedAt == null, cancellationToken);
        if (enrollment is not null && string.IsNullOrWhiteSpace(request.TotpCode))
        {
            var ticket = secrets.UrlSafe(24);
            await audit.WriteAsync(tenant.Id, "mfa.challenge", user.Id, null, context, null, cancellationToken);
            return new LoginResponse(user.Id, tenant.Id, user.Email, user.DisplayName, true, ticket, null, null);
        }

        if (enrollment is not null && !totp.Verify(enrollment.Secret, request.TotpCode!))
        {
            await audit.WriteAsync(tenant.Id, "mfa.failure", user.Id, null, context, null, cancellationToken);
            throw new InvalidOperationException("Invalid MFA code.");
        }

        user.ResetFailures();
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(tenant.Id, "login.success", user.Id, null, context, null, cancellationToken);
        var dashboardApp = await db.Applications.SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.ClientId == "netauth0_admin_spa", cancellationToken);
        var tokens = dashboardApp is null
            ? null
            : await tokenIssuer.IssueUserTokensAsync(user, dashboardApp, "openid profile email offline_access", null, cancellationToken);

        return new LoginResponse(user.Id, tenant.Id, user.Email, user.DisplayName, false, null, tokens?.AccessToken, tokens?.IdToken);
    }

    public async Task<MfaEnrollmentResponse> BeginTotpEnrollmentAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleAsync(x => x.Id == userId, cancellationToken);
        var secret = totp.CreateSecret();
        var enrollment = new MfaEnrollment(userId, secret);
        db.MfaEnrollments.Add(enrollment);

        var recoveryCodes = Enumerable.Range(0, 10).Select(_ => secrets.UrlSafe(10)).ToArray();
        foreach (var code in recoveryCodes)
        {
            db.RecoveryCodes.Add(new RecoveryCode(userId, secrets.Sha256Base64Url(code)));
        }

        await db.SaveChangesAsync(cancellationToken);
        return new MfaEnrollmentResponse(enrollment.Id, secret, totp.BuildOtpAuthUri("NetAuth0", user.Email, secret), recoveryCodes);
    }

    public async Task VerifyTotpEnrollmentAsync(VerifyMfaRequest request, CancellationToken cancellationToken)
    {
        var enrollment = await db.MfaEnrollments.SingleAsync(x => x.Id == request.EnrollmentId, cancellationToken);
        if (!totp.Verify(enrollment.Secret, request.Code))
        {
            throw new InvalidOperationException("Invalid MFA code.");
        }

        enrollment.Verify(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await db.RefreshTokens
            .Where(x => x.UserId == userId && x.RotatedFromId == null)
            .Join(db.Applications, rt => rt.ClientApplicationId, app => app.Id, (rt, app) => new SessionDto(rt.Id, rt.FamilyId, app.Name, rt.CreatedAt, rt.ExpiresAt, rt.RevokedAt))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task RevokeSessionFamilyAsync(Guid userId, Guid familyId, RequestContext context, CancellationToken cancellationToken)
    {
        var tokens = await db.RefreshTokens.Where(x => x.UserId == userId && x.FamilyId == familyId).ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.Revoke(clock.UtcNow);
        }
        await db.SaveChangesAsync(cancellationToken);
        var tenantId = tokens.FirstOrDefault()?.TenantId;
        if (tenantId is not null)
        {
            await audit.WriteAsync(tenantId.Value, "refresh_token.family_revoked", userId, null, context, new { familyId }, cancellationToken);
        }
    }
}
