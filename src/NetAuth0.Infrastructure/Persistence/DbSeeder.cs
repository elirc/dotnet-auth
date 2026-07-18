using Microsoft.EntityFrameworkCore;
using NetAuth0.Application.Abstractions;
using NetAuth0.Domain.Identity;

namespace NetAuth0.Infrastructure.Persistence;

public sealed class DbSeeder(AppDbContext db, IPasswordHasher passwordHasher)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.Tenants.AnyAsync(cancellationToken))
        {
            return;
        }

        var tenant = new Tenant("default", "Default Tenant");
        db.Tenants.Add(tenant);

        var admin = new User(tenant.Id, "admin@netauth0.local", "NetAuth0 Admin", passwordHasher.Hash("ChangeMe123!"));
        admin.MarkEmailVerified();
        db.Users.Add(admin);

        var spa = new ClientApplication(tenant.Id, "NetAuth0 Admin Dashboard", ClientApplicationType.Spa, "netauth0_admin_spa", null);
        spa.Configure(requirePkce: true, requireConsent: false, accessTokenLifetimeMinutes: 15, refreshTokenLifetimeDays: 14);
        spa.RedirectUris.Add(new ApplicationRedirectUri(spa.Id, "http://localhost:5173/callback"));
        spa.GrantTypes.Add(new ApplicationGrantType(spa.Id, GrantType.AuthorizationCode));
        spa.GrantTypes.Add(new ApplicationGrantType(spa.Id, GrantType.RefreshToken));
        db.Applications.Add(spa);

        var role = new Role(tenant.Id, spa.Id, "admin");
        var permissions = new[]
        {
            new Permission(tenant.Id, spa.Id, "users:manage", "Manage users"),
            new Permission(tenant.Id, spa.Id, "applications:manage", "Manage applications"),
            new Permission(tenant.Id, spa.Id, "roles:manage", "Manage roles"),
            new Permission(tenant.Id, spa.Id, "audit:read", "Read audit logs")
        };
        db.Permissions.AddRange(permissions);
        foreach (var permission in permissions)
        {
            role.Permissions.Add(new RolePermission(role.Id, permission.Id));
        }
        db.Roles.Add(role);
        db.UserRoles.Add(new UserRole(admin.Id, role.Id));

        await db.SaveChangesAsync(cancellationToken);
    }
}
