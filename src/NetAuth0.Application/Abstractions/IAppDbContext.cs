using Microsoft.EntityFrameworkCore;
using NetAuth0.Domain.Identity;

namespace NetAuth0.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<ClientApplication> Applications { get; }
    DbSet<ApplicationRedirectUri> ApplicationRedirectUris { get; }
    DbSet<ApplicationGrantType> ApplicationGrantTypes { get; }
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<AuthorizationCode> AuthorizationCodes { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<SigningKey> SigningKeys { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DbSet<MfaEnrollment> MfaEnrollments { get; }
    DbSet<RecoveryCode> RecoveryCodes { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
