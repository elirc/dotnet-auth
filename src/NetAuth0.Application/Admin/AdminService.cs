using Microsoft.EntityFrameworkCore;
using NetAuth0.Application.Abstractions;
using NetAuth0.Application.Audit;
using NetAuth0.Application.Common;
using NetAuth0.Domain.Identity;

namespace NetAuth0.Application.Admin;

public interface IAdminService
{
    Task<CreatedApplicationResponse> CreateApplicationAsync(CreateApplicationRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApplicationDto>> ListApplicationsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<PagedResult<UserDto>> SearchUsersAsync(Guid tenantId, string? query, int page, int pageSize, CancellationToken cancellationToken);
    Task SetUserBlockedAsync(Guid tenantId, Guid userId, bool blocked, RequestContext context, CancellationToken cancellationToken);
    Task ForceLogoutAsync(Guid tenantId, Guid userId, RequestContext context, CancellationToken cancellationToken);
    Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, RequestContext context, CancellationToken cancellationToken);
    Task AssignRoleAsync(AssignRoleRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<PagedResult<AuditEventDto>> SearchAuditEventsAsync(Guid tenantId, string? eventType, string? actor, int page, int pageSize, CancellationToken cancellationToken);
}

public sealed class AdminService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    ISecretGenerator secrets,
    IClock clock,
    IAuditWriter audit) : IAdminService
{
    public async Task<CreatedApplicationResponse> CreateApplicationAsync(CreateApplicationRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        if (!await db.Tenants.AnyAsync(x => x.Id == request.TenantId, cancellationToken))
        {
            throw new InvalidOperationException("Tenant not found.");
        }

        var clientId = $"na0_{secrets.UrlSafe(18)}";
        var secret = request.Type == ClientApplicationType.Spa ? null : secrets.UrlSafe(36);
        var app = new ClientApplication(request.TenantId, request.Name, request.Type, clientId, secret is null ? null : passwordHasher.Hash(secret));
        app.Configure(requirePkce: request.Type != ClientApplicationType.MachineToMachine, request.RequireConsent, 15, Math.Clamp(request.RefreshTokenLifetimeDays, 1, 365));

        foreach (var uri in request.RedirectUris.Distinct(StringComparer.Ordinal))
        {
            app.RedirectUris.Add(new ApplicationRedirectUri(app.Id, uri));
        }

        foreach (var grant in request.GrantTypes.Distinct())
        {
            app.GrantTypes.Add(new ApplicationGrantType(app.Id, grant));
        }

        db.Applications.Add(app);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(request.TenantId, "admin.application_created", null, clientId, context, new { request.Name, request.Type }, cancellationToken);
        return new CreatedApplicationResponse(clientId, secret);
    }

    public async Task<IReadOnlyList<ApplicationDto>> ListApplicationsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return await db.Applications
            .Where(x => x.TenantId == tenantId)
            .Include(x => x.RedirectUris)
            .Include(x => x.GrantTypes)
            .OrderBy(x => x.Name)
            .Select(x => new ApplicationDto(
                x.Id,
                x.Name,
                x.ClientId,
                x.Type.ToString(),
                x.RedirectUris.Select(r => r.Uri).ToList(),
                x.GrantTypes.Select(g => g.GrantType.ToString()).ToList()))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<UserDto>> SearchUsersAsync(Guid tenantId, string? query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var users = db.Users.Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLowerInvariant();
            users = users.Where(x => x.Email.Contains(q) || x.DisplayName.ToLower().Contains(q));
        }

        var projection = users.OrderByDescending(x => x.CreatedAt).Select(x => new UserDto(x.Id, x.Email, x.DisplayName, x.EmailVerified, x.IsBlocked, x.CreatedAt));
        return await PagedResult<UserDto>.CreateAsync(
            projection,
            page,
            pageSize,
            q => q.ToListAsync(cancellationToken),
            q => q.CountAsync(cancellationToken));
    }

    public async Task SetUserBlockedAsync(Guid tenantId, Guid userId, bool blocked, RequestContext context, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleAsync(x => x.TenantId == tenantId && x.Id == userId, cancellationToken);
        if (blocked) user.Block(); else user.Unblock();
        user.Touch(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(tenantId, blocked ? "admin.user_blocked" : "admin.user_unblocked", userId, null, context, null, cancellationToken);
    }

    public async Task ForceLogoutAsync(Guid tenantId, Guid userId, RequestContext context, CancellationToken cancellationToken)
    {
        var tokens = await db.RefreshTokens.Where(x => x.TenantId == tenantId && x.UserId == userId && x.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.Revoke(clock.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(tenantId, "admin.user_force_logout", userId, null, context, new { count = tokens.Count }, cancellationToken);
    }

    public async Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var role = new Role(request.TenantId, request.ApplicationId, request.Name);
        db.Roles.Add(role);

        foreach (var name in request.Permissions.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal))
        {
            var permission = await db.Permissions.SingleOrDefaultAsync(x => x.ClientApplicationId == request.ApplicationId && x.Name == name, cancellationToken);
            if (permission is null)
            {
                permission = new Permission(request.TenantId, request.ApplicationId, name, name);
                db.Permissions.Add(permission);
            }

            role.Permissions.Add(new RolePermission(role.Id, permission.Id));
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(request.TenantId, "admin.role_created", null, null, context, new { request.Name, request.ApplicationId }, cancellationToken);
        return new RoleDto(role.Id, role.Name, request.Permissions);
    }

    public async Task AssignRoleAsync(AssignRoleRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleAsync(x => x.Id == request.UserId, cancellationToken);
        var role = await db.Roles.SingleAsync(x => x.Id == request.RoleId, cancellationToken);
        if (!await db.UserRoles.AnyAsync(x => x.UserId == request.UserId && x.RoleId == request.RoleId, cancellationToken))
        {
            db.UserRoles.Add(new UserRole(request.UserId, request.RoleId));
            await db.SaveChangesAsync(cancellationToken);
        }

        await audit.WriteAsync(user.TenantId, "admin.role_assigned", user.Id, null, context, new { role.Name }, cancellationToken);
    }

    public async Task<PagedResult<AuditEventDto>> SearchAuditEventsAsync(Guid tenantId, string? eventType, string? actor, int page, int pageSize, CancellationToken cancellationToken)
    {
        var events = db.AuditEvents.Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            events = events.Where(x => x.EventType.Contains(eventType));
        }

        if (Guid.TryParse(actor, out var actorId))
        {
            events = events.Where(x => x.ActorUserId == actorId);
        }
        else if (!string.IsNullOrWhiteSpace(actor))
        {
            events = events.Where(x => x.ActorClientId == actor);
        }

        var projection = events.OrderByDescending(x => x.CreatedAt)
            .Select(x => new AuditEventDto(x.Id, x.CreatedAt, x.EventType, x.ActorUserId, x.ActorClientId, x.IpAddress, x.UserAgent, x.DataJson));

        return await PagedResult<AuditEventDto>.CreateAsync(
            projection,
            page,
            pageSize,
            q => q.ToListAsync(cancellationToken),
            q => q.CountAsync(cancellationToken));
    }
}
