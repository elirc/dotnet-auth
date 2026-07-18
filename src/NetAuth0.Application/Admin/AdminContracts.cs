using NetAuth0.Domain.Identity;

namespace NetAuth0.Application.Admin;

public sealed record CreateApplicationRequest(
    Guid TenantId,
    string Name,
    ClientApplicationType Type,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<GrantType> GrantTypes,
    bool RequireConsent,
    int RefreshTokenLifetimeDays);

public sealed record CreatedApplicationResponse(string ClientId, string? ClientSecret);
public sealed record ApplicationDto(Guid Id, string Name, string ClientId, string Type, IReadOnlyList<string> RedirectUris, IReadOnlyList<string> GrantTypes);
public sealed record UserDto(Guid Id, string Email, string DisplayName, bool EmailVerified, bool IsBlocked, DateTimeOffset CreatedAt);
public sealed record AuditEventDto(Guid Id, DateTimeOffset OccurredAt, string EventType, Guid? ActorUserId, string? ActorClientId, string? IpAddress, string? UserAgent, string DataJson);
public sealed record RoleDto(Guid Id, string Name, IReadOnlyList<string> Permissions);
public sealed record CreateRoleRequest(Guid TenantId, Guid ApplicationId, string Name, IReadOnlyList<string> Permissions);
public sealed record AssignRoleRequest(Guid UserId, Guid RoleId);
