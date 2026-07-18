using System.Text;
using Microsoft.EntityFrameworkCore;
using NetAuth0.Application.Abstractions;
using NetAuth0.Application.Audit;
using NetAuth0.Domain.Identity;

namespace NetAuth0.Application.OAuth;

public interface IOAuthService
{
    Task<AuthorizationRedirect> CreateAuthorizationCodeAsync(AuthorizeRequest request, Guid userId, RequestContext context, CancellationToken cancellationToken);
    Task<TokenResponse> ExchangeTokenAsync(TokenRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<SessionDto>> GetSessionsAsync(Guid userId, CancellationToken cancellationToken);
    Task RevokeRefreshTokenFamilyAsync(Guid userId, Guid familyId, RequestContext context, CancellationToken cancellationToken);
}

public sealed class OAuthService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    ISecretGenerator secrets,
    ITokenIssuer tokenIssuer,
    IClock clock,
    IAuditWriter audit) : IOAuthService
{
    public async Task<AuthorizationRedirect> CreateAuthorizationCodeAsync(AuthorizeRequest request, Guid userId, RequestContext context, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.ResponseType, "code", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only response_type=code is supported.");
        }

        var app = await GetClientAsync(request.ClientId, GrantType.AuthorizationCode, cancellationToken);
        ValidateRedirectUri(app, request.RedirectUri);
        ValidatePkce(app, request.CodeChallenge, request.CodeChallengeMethod);

        var user = await db.Users.SingleAsync(x => x.Id == userId && x.TenantId == app.TenantId, cancellationToken);
        if (user.IsBlocked)
        {
            throw new InvalidOperationException("User is blocked.");
        }

        var code = secrets.UrlSafe(32);
        var entity = new AuthorizationCode(
            secrets.Sha256Base64Url(code),
            app.TenantId,
            app.Id,
            user.Id,
            request.RedirectUri,
            request.Scope,
            request.Nonce,
            request.CodeChallenge,
            request.CodeChallengeMethod,
            clock.UtcNow.AddMinutes(5));

        db.AuthorizationCodes.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(app.TenantId, "authorization_code.issued", user.Id, app.ClientId, context, new { app.Name, request.Scope }, cancellationToken);

        var separator = request.RedirectUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var redirect = new StringBuilder(request.RedirectUri)
            .Append(separator)
            .Append("code=")
            .Append(Uri.EscapeDataString(code))
            .Append("&state=")
            .Append(Uri.EscapeDataString(request.State))
            .ToString();

        return new AuthorizationRedirect(redirect);
    }

    public async Task<TokenResponse> ExchangeTokenAsync(TokenRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        return request.GrantType switch
        {
            "authorization_code" => await ExchangeAuthorizationCodeAsync(request, context, cancellationToken),
            "client_credentials" => await ExchangeClientCredentialsAsync(request, context, cancellationToken),
            "refresh_token" => await ExchangeRefreshTokenAsync(request, context, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported grant_type.")
        };
    }

    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await db.RefreshTokens
            .Where(x => x.UserId == userId && x.RotatedFromId == null)
            .Join(db.Applications, rt => rt.ClientApplicationId, app => app.Id, (rt, app) => new SessionDto(rt.Id, rt.FamilyId, app.Name, rt.CreatedAt, rt.ExpiresAt, rt.RevokedAt))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task RevokeRefreshTokenFamilyAsync(Guid userId, Guid familyId, RequestContext context, CancellationToken cancellationToken)
    {
        var family = await db.RefreshTokens.Where(x => x.UserId == userId && x.FamilyId == familyId).ToListAsync(cancellationToken);
        foreach (var token in family)
        {
            token.Revoke(clock.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);
        var tenantId = family.FirstOrDefault()?.TenantId;
        if (tenantId is not null)
        {
            await audit.WriteAsync(tenantId.Value, "refresh_token.family_revoked", userId, null, context, new { familyId }, cancellationToken);
        }
    }

    private async Task<TokenResponse> ExchangeAuthorizationCodeAsync(TokenRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.RedirectUri) || string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.CodeVerifier))
        {
            throw new InvalidOperationException("code, redirect_uri, client_id, and code_verifier are required.");
        }

        var app = await GetClientAsync(request.ClientId, GrantType.AuthorizationCode, cancellationToken);
        await ValidateClientSecretIfRequiredAsync(app, request.ClientSecret);

        var codeHash = secrets.Sha256Base64Url(request.Code);
        var code = await db.AuthorizationCodes
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.CodeHash == codeHash && x.ClientApplicationId == app.Id, cancellationToken)
            ?? throw new InvalidOperationException("Invalid authorization code.");

        if (!code.IsActive(clock.UtcNow))
        {
            throw new InvalidOperationException("Authorization code is expired or already used.");
        }

        if (!string.Equals(code.RedirectUri, request.RedirectUri, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("redirect_uri mismatch.");
        }

        var expectedChallenge = code.CodeChallengeMethod switch
        {
            "S256" => secrets.Sha256Base64Url(request.CodeVerifier),
            "plain" => request.CodeVerifier,
            _ => throw new InvalidOperationException("Unsupported code_challenge_method.")
        };

        if (!secrets.FixedTimeEquals(expectedChallenge, code.CodeChallenge))
        {
            throw new InvalidOperationException("PKCE verification failed.");
        }

        code.Consume(clock.UtcNow);
        var jwt = await tokenIssuer.IssueUserTokensAsync(code.User, app, code.Scope, code.Nonce, cancellationToken);
        var refresh = CreateRefreshToken(app, code.User.Id, code.Scope, null);
        db.RefreshTokens.Add(refresh.Entity);
        await db.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(app.TenantId, "token.issued", code.UserId, app.ClientId, context, new { grant = "authorization_code", code.Scope }, cancellationToken);
        return new TokenResponse(jwt.AccessToken, "Bearer", jwt.ExpiresIn, jwt.IdToken, refresh.Secret, code.Scope);
    }

    private async Task<TokenResponse> ExchangeClientCredentialsAsync(TokenRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            throw new InvalidOperationException("client_id and client_secret are required.");
        }

        var app = await GetClientAsync(request.ClientId, GrantType.ClientCredentials, cancellationToken);
        await ValidateClientSecretIfRequiredAsync(app, request.ClientSecret);

        var permissions = await db.Permissions
            .Where(x => x.ClientApplicationId == app.Id)
            .Select(x => x.Name)
            .ToListAsync(cancellationToken);

        var accessToken = await tokenIssuer.IssueClientAccessTokenAsync(app, permissions, cancellationToken);
        await audit.WriteAsync(app.TenantId, "token.issued", null, app.ClientId, context, new { grant = "client_credentials", request.Scope }, cancellationToken);
        return new TokenResponse(accessToken, "Bearer", app.AccessTokenLifetimeMinutes * 60, null, null, request.Scope);
    }

    private async Task<TokenResponse> ExchangeRefreshTokenAsync(TokenRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || string.IsNullOrWhiteSpace(request.ClientId))
        {
            throw new InvalidOperationException("refresh_token and client_id are required.");
        }

        var app = await GetClientAsync(request.ClientId, GrantType.RefreshToken, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            await ValidateClientSecretIfRequiredAsync(app, request.ClientSecret);
        }

        var tokenHash = secrets.Sha256Base64Url(request.RefreshToken);
        var existing = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == tokenHash && x.ClientApplicationId == app.Id, cancellationToken)
            ?? throw new InvalidOperationException("Invalid refresh token.");

        if (existing.ExpiresAt <= clock.UtcNow)
        {
            existing.Revoke(clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Refresh token expired.");
        }

        if (!existing.IsActive(clock.UtcNow))
        {
            await RevokeFamilyForReuseAsync(existing, context, cancellationToken);
            throw new InvalidOperationException("Refresh token reuse detected.");
        }

        var user = existing.UserId is null
            ? null
            : await db.Users.SingleAsync(x => x.Id == existing.UserId, cancellationToken);

        var replacement = CreateRefreshToken(app, existing.UserId, existing.Scope, existing.Id, existing.FamilyId);
        db.RefreshTokens.Add(replacement.Entity);
        await db.SaveChangesAsync(cancellationToken);
        existing.RotateTo(replacement.Entity.Id, clock.UtcNow);

        TokenResponse response;
        if (user is not null)
        {
            var jwt = await tokenIssuer.IssueUserTokensAsync(user, app, existing.Scope, null, cancellationToken);
            response = new TokenResponse(jwt.AccessToken, "Bearer", jwt.ExpiresIn, jwt.IdToken, replacement.Secret, existing.Scope);
        }
        else
        {
            var permissions = await db.Permissions.Where(x => x.ClientApplicationId == app.Id).Select(x => x.Name).ToListAsync(cancellationToken);
            var accessToken = await tokenIssuer.IssueClientAccessTokenAsync(app, permissions, cancellationToken);
            response = new TokenResponse(accessToken, "Bearer", app.AccessTokenLifetimeMinutes * 60, null, replacement.Secret, existing.Scope);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(app.TenantId, "refresh_token.rotated", existing.UserId, app.ClientId, context, new { existing.FamilyId }, cancellationToken);
        return response;
    }

    private async Task<ClientApplication> GetClientAsync(string clientId, GrantType grantType, CancellationToken cancellationToken)
    {
        var app = await db.Applications
            .Include(x => x.RedirectUris)
            .Include(x => x.GrantTypes)
            .SingleOrDefaultAsync(x => x.ClientId == clientId, cancellationToken)
            ?? throw new InvalidOperationException("Unknown client.");

        if (!app.GrantTypes.Any(x => x.GrantType == grantType))
        {
            throw new InvalidOperationException("Grant type is not allowed for this client.");
        }

        return app;
    }

    private static void ValidateRedirectUri(ClientApplication app, string redirectUri)
    {
        if (!app.RedirectUris.Any(x => string.Equals(x.Uri, redirectUri, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("redirect_uri is not registered.");
        }
    }

    private static void ValidatePkce(ClientApplication app, string codeChallenge, string method)
    {
        if (app.RequirePkce && string.IsNullOrWhiteSpace(codeChallenge))
        {
            throw new InvalidOperationException("PKCE code_challenge is required.");
        }

        if (app.RequirePkce && method is not ("S256" or "plain"))
        {
            throw new InvalidOperationException("Unsupported code_challenge_method.");
        }
    }

    private Task ValidateClientSecretIfRequiredAsync(ClientApplication app, string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(app.ClientSecretHash))
        {
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(clientSecret) || !passwordHasher.Verify(clientSecret, app.ClientSecretHash))
        {
            throw new InvalidOperationException("Invalid client credentials.");
        }

        return Task.CompletedTask;
    }

    private (RefreshToken Entity, string Secret) CreateRefreshToken(ClientApplication app, Guid? userId, string scope, Guid? rotatedFromId, Guid? familyId = null)
    {
        var secret = secrets.UrlSafe(48);
        var entity = new RefreshToken(
            secrets.Sha256Base64Url(secret),
            familyId ?? Guid.NewGuid(),
            app.TenantId,
            app.Id,
            userId,
            scope,
            clock.UtcNow.AddDays(app.RefreshTokenLifetimeDays),
            rotatedFromId);

        return (entity, secret);
    }

    private async Task RevokeFamilyForReuseAsync(RefreshToken token, RequestContext context, CancellationToken cancellationToken)
    {
        var family = await db.RefreshTokens.Where(x => x.FamilyId == token.FamilyId).ToListAsync(cancellationToken);
        foreach (var item in family)
        {
            item.MarkReuse(clock.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(token.TenantId, "refresh_token.reuse_detected", token.UserId, null, context, new { token.FamilyId }, cancellationToken);
    }
}
