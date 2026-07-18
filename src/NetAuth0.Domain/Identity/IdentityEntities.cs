using NetAuth0.Domain.Common;

namespace NetAuth0.Domain.Identity;

public enum ClientApplicationType
{
    Spa = 1,
    WebApp = 2,
    MachineToMachine = 3
}

public enum GrantType
{
    AuthorizationCode = 1,
    ClientCredentials = 2,
    RefreshToken = 3
}

public sealed class Tenant : Entity
{
    private Tenant() { }

    public Tenant(string slug, string name)
    {
        Slug = slug;
        Name = name;
    }

    public string Slug { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public ICollection<ClientApplication> Applications { get; private set; } = new List<ClientApplication>();
    public ICollection<User> Users { get; private set; } = new List<User>();
}

public sealed class ClientApplication : Entity
{
    private ClientApplication() { }

    public ClientApplication(Guid tenantId, string name, ClientApplicationType type, string clientId, string? clientSecretHash)
    {
        TenantId = tenantId;
        Name = name;
        Type = type;
        ClientId = clientId;
        ClientSecretHash = clientSecretHash;
    }

    public Guid TenantId { get; private set; }
    public Tenant Tenant { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public ClientApplicationType Type { get; private set; }
    public string ClientId { get; private set; } = string.Empty;
    public string? ClientSecretHash { get; private set; }
    public bool RequirePkce { get; private set; } = true;
    public bool RequireConsent { get; private set; } = true;
    public int AccessTokenLifetimeMinutes { get; private set; } = 15;
    public int IdTokenLifetimeMinutes { get; private set; } = 15;
    public int RefreshTokenLifetimeDays { get; private set; } = 30;
    public ICollection<ApplicationRedirectUri> RedirectUris { get; private set; } = new List<ApplicationRedirectUri>();
    public ICollection<ApplicationGrantType> GrantTypes { get; private set; } = new List<ApplicationGrantType>();

    public void Configure(bool requirePkce, bool requireConsent, int accessTokenLifetimeMinutes, int refreshTokenLifetimeDays)
    {
        RequirePkce = requirePkce;
        RequireConsent = requireConsent;
        AccessTokenLifetimeMinutes = accessTokenLifetimeMinutes;
        IdTokenLifetimeMinutes = accessTokenLifetimeMinutes;
        RefreshTokenLifetimeDays = refreshTokenLifetimeDays;
    }
}

public sealed class ApplicationRedirectUri : Entity
{
    private ApplicationRedirectUri() { }
    public ApplicationRedirectUri(Guid applicationId, string uri)
    {
        ClientApplicationId = applicationId;
        Uri = uri;
    }

    public Guid ClientApplicationId { get; private set; }
    public ClientApplication ClientApplication { get; private set; } = null!;
    public string Uri { get; private set; } = string.Empty;
}

public sealed class ApplicationGrantType : Entity
{
    private ApplicationGrantType() { }
    public ApplicationGrantType(Guid applicationId, GrantType grantType)
    {
        ClientApplicationId = applicationId;
        GrantType = grantType;
    }

    public Guid ClientApplicationId { get; private set; }
    public ClientApplication ClientApplication { get; private set; } = null!;
    public GrantType GrantType { get; private set; }
}

public sealed class User : Entity
{
    private User() { }

    public User(Guid tenantId, string email, string displayName, string passwordHash)
    {
        TenantId = tenantId;
        Email = email.Trim().ToLowerInvariant();
        DisplayName = displayName;
        PasswordHash = passwordHash;
    }

    public Guid TenantId { get; private set; }
    public Tenant Tenant { get; private set; } = null!;
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool EmailVerified { get; private set; }
    public bool IsBlocked { get; private set; }
    public int FailedAccessCount { get; private set; }
    public DateTimeOffset? LockoutEndsAt { get; private set; }
    public string SecurityStamp { get; private set; } = Guid.NewGuid().ToString("N");
    public ICollection<UserRole> Roles { get; private set; } = new List<UserRole>();

    public void MarkEmailVerified() => EmailVerified = true;
    public void Block() => IsBlocked = true;
    public void Unblock() => IsBlocked = false;
    public void ResetFailures() { FailedAccessCount = 0; LockoutEndsAt = null; }
    public void RegisterFailure(DateTimeOffset now, int maxAttempts, TimeSpan lockout)
    {
        FailedAccessCount++;
        if (FailedAccessCount >= maxAttempts)
        {
            LockoutEndsAt = now.Add(lockout);
        }
    }
}

public sealed class Role : Entity
{
    private Role() { }
    public Role(Guid tenantId, Guid applicationId, string name)
    {
        TenantId = tenantId;
        ClientApplicationId = applicationId;
        Name = name;
    }

    public Guid TenantId { get; private set; }
    public Guid ClientApplicationId { get; private set; }
    public ClientApplication ClientApplication { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public ICollection<RolePermission> Permissions { get; private set; } = new List<RolePermission>();
}

public sealed class Permission : Entity
{
    private Permission() { }
    public Permission(Guid tenantId, Guid applicationId, string name, string description)
    {
        TenantId = tenantId;
        ClientApplicationId = applicationId;
        Name = name;
        Description = description;
    }

    public Guid TenantId { get; private set; }
    public Guid ClientApplicationId { get; private set; }
    public ClientApplication ClientApplication { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
}

public sealed class RolePermission
{
    private RolePermission() { }
    public RolePermission(Guid roleId, Guid permissionId)
    {
        RoleId = roleId;
        PermissionId = permissionId;
    }

    public Guid RoleId { get; private set; }
    public Role Role { get; private set; } = null!;
    public Guid PermissionId { get; private set; }
    public Permission Permission { get; private set; } = null!;
}

public sealed class UserRole
{
    private UserRole() { }
    public UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;
    public Guid RoleId { get; private set; }
    public Role Role { get; private set; } = null!;
}

public sealed class AuthorizationCode : Entity
{
    private AuthorizationCode() { }
    public AuthorizationCode(string codeHash, Guid tenantId, Guid applicationId, Guid userId, string redirectUri, string scope, string? nonce, string codeChallenge, string codeChallengeMethod, DateTimeOffset expiresAt)
    {
        CodeHash = codeHash;
        TenantId = tenantId;
        ClientApplicationId = applicationId;
        UserId = userId;
        RedirectUri = redirectUri;
        Scope = scope;
        Nonce = nonce;
        CodeChallenge = codeChallenge;
        CodeChallengeMethod = codeChallengeMethod;
        ExpiresAt = expiresAt;
    }

    public string CodeHash { get; private set; } = string.Empty;
    public Guid TenantId { get; private set; }
    public Guid ClientApplicationId { get; private set; }
    public ClientApplication ClientApplication { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;
    public string RedirectUri { get; private set; } = string.Empty;
    public string Scope { get; private set; } = string.Empty;
    public string? Nonce { get; private set; }
    public string CodeChallenge { get; private set; } = string.Empty;
    public string CodeChallengeMethod { get; private set; } = "S256";
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => ConsumedAt is null && ExpiresAt > now;
    public void Consume(DateTimeOffset now) => ConsumedAt = now;
}

public sealed class RefreshToken : Entity
{
    private RefreshToken() { }
    public RefreshToken(string tokenHash, Guid familyId, Guid tenantId, Guid applicationId, Guid? userId, string scope, DateTimeOffset expiresAt, Guid? rotatedFromId = null)
    {
        TokenHash = tokenHash;
        FamilyId = familyId;
        TenantId = tenantId;
        ClientApplicationId = applicationId;
        UserId = userId;
        Scope = scope;
        ExpiresAt = expiresAt;
        RotatedFromId = rotatedFromId;
    }

    public string TokenHash { get; private set; } = string.Empty;
    public Guid FamilyId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ClientApplicationId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Scope { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public Guid? RotatedFromId { get; private set; }
    public Guid? ReplacedById { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? ReuseDetectedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ReuseDetectedAt is null && ExpiresAt > now && ReplacedById is null;
    public void RotateTo(Guid replacementId, DateTimeOffset now)
    {
        ReplacedById = replacementId;
        RevokedAt = now;
    }
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
    public void MarkReuse(DateTimeOffset now)
    {
        ReuseDetectedAt ??= now;
        RevokedAt ??= now;
    }
}

public sealed class SigningKey : Entity
{
    private SigningKey() { }
    public SigningKey(string keyId, string privateKeyPem, DateTimeOffset notBefore, DateTimeOffset? retiredAt = null)
    {
        KeyId = keyId;
        PrivateKeyPem = privateKeyPem;
        NotBefore = notBefore;
        RetiredAt = retiredAt;
    }

    public string KeyId { get; private set; } = string.Empty;
    public string PrivateKeyPem { get; private set; } = string.Empty;
    public DateTimeOffset NotBefore { get; private set; }
    public DateTimeOffset? RetiredAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public void Retire(DateTimeOffset now) => RetiredAt = now;
}

public sealed class AuditEvent : Entity
{
    private AuditEvent() { }
    public AuditEvent(Guid tenantId, string eventType, Guid? actorUserId, string? actorClientId, string? ipAddress, string? userAgent, string dataJson)
    {
        TenantId = tenantId;
        EventType = eventType;
        ActorUserId = actorUserId;
        ActorClientId = actorClientId;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        DataJson = dataJson;
    }

    public Guid TenantId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public Guid? ActorUserId { get; private set; }
    public string? ActorClientId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string DataJson { get; private set; } = "{}";
}

public sealed class MfaEnrollment : Entity
{
    private MfaEnrollment() { }
    public MfaEnrollment(Guid userId, string secret)
    {
        UserId = userId;
        Secret = secret;
    }

    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;
    public string Type { get; private set; } = "totp";
    public string Secret { get; private set; } = string.Empty;
    public DateTimeOffset? VerifiedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public void Verify(DateTimeOffset now) => VerifiedAt = now;
    public void Revoke(DateTimeOffset now) => RevokedAt = now;
}

public sealed class RecoveryCode : Entity
{
    private RecoveryCode() { }
    public RecoveryCode(Guid userId, string codeHash)
    {
        UserId = userId;
        CodeHash = codeHash;
    }

    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;
    public string CodeHash { get; private set; } = string.Empty;
    public DateTimeOffset? UsedAt { get; private set; }
    public void Use(DateTimeOffset now) => UsedAt = now;
}
