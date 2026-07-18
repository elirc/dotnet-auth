using Microsoft.EntityFrameworkCore;
using NetAuth0.Application.Abstractions;
using NetAuth0.Domain.Identity;

namespace NetAuth0.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<ClientApplication> Applications => Set<ClientApplication>();
    public DbSet<ApplicationRedirectUri> ApplicationRedirectUris => Set<ApplicationRedirectUri>();
    public DbSet<ApplicationGrantType> ApplicationGrantTypes => Set<ApplicationGrantType>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AuthorizationCode> AuthorizationCodes => Set<AuthorizationCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SigningKey> SigningKeys => Set<SigningKey>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<MfaEnrollment> MfaEnrollments => Set<MfaEnrollment>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.Entity<Tenant>(b =>
        {
            b.HasIndex(x => x.Slug).IsUnique();
            b.Property(x => x.Slug).HasMaxLength(80);
            b.Property(x => x.Name).HasMaxLength(200);
            b.UseXminAsConcurrencyToken();
        });

        modelBuilder.Entity<ClientApplication>(b =>
        {
            b.ToTable("Applications");
            b.HasIndex(x => x.ClientId).IsUnique();
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.ClientId).HasMaxLength(120);
            b.Property(x => x.ClientSecretHash).HasMaxLength(300);
            b.HasMany(x => x.RedirectUris).WithOne(x => x.ClientApplication).HasForeignKey(x => x.ClientApplicationId);
            b.HasMany(x => x.GrantTypes).WithOne(x => x.ClientApplication).HasForeignKey(x => x.ClientApplicationId);
            b.UseXminAsConcurrencyToken();
        });

        modelBuilder.Entity<ApplicationRedirectUri>(b =>
        {
            b.HasIndex(x => new { x.ClientApplicationId, x.Uri }).IsUnique();
            b.Property(x => x.Uri).HasMaxLength(2048);
        });

        modelBuilder.Entity<ApplicationGrantType>(b =>
        {
            b.HasIndex(x => new { x.ClientApplicationId, x.GrantType }).IsUnique();
        });

        modelBuilder.Entity<User>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            b.Property(x => x.Email).HasMaxLength(320);
            b.Property(x => x.DisplayName).HasMaxLength(200);
            b.Property(x => x.PasswordHash).HasMaxLength(300);
            b.UseXminAsConcurrencyToken();
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.HasIndex(x => new { x.ClientApplicationId, x.Name }).IsUnique();
            b.HasMany(x => x.Permissions).WithOne(x => x.Role).HasForeignKey(x => x.RoleId);
        });

        modelBuilder.Entity<Permission>(b =>
        {
            b.HasIndex(x => new { x.ClientApplicationId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<UserRole>(b =>
        {
            b.HasKey(x => new { x.UserId, x.RoleId });
            b.HasOne(x => x.User).WithMany(x => x.Roles).HasForeignKey(x => x.UserId);
            b.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
        });

        modelBuilder.Entity<RolePermission>(b =>
        {
            b.HasKey(x => new { x.RoleId, x.PermissionId });
            b.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId);
        });

        modelBuilder.Entity<AuthorizationCode>(b =>
        {
            b.HasIndex(x => x.CodeHash).IsUnique();
            b.Property(x => x.CodeHash).HasMaxLength(100);
            b.Property(x => x.RedirectUri).HasMaxLength(2048);
            b.Property(x => x.Scope).HasMaxLength(1000);
            b.Property(x => x.CodeChallenge).HasMaxLength(256);
        });

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => x.FamilyId);
            b.Property(x => x.Scope).HasMaxLength(1000);
        });

        modelBuilder.Entity<SigningKey>(b =>
        {
            b.HasIndex(x => x.KeyId).IsUnique();
            b.Property(x => x.PrivateKeyPem).HasColumnType("text");
        });

        modelBuilder.Entity<AuditEvent>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.CreatedAt });
            b.HasIndex(x => x.EventType);
            b.Property(x => x.DataJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<MfaEnrollment>(b =>
        {
            b.HasIndex(x => new { x.UserId, x.Type });
        });

        modelBuilder.Entity<RecoveryCode>(b =>
        {
            b.HasIndex(x => new { x.UserId, x.CodeHash }).IsUnique();
        });
    }
}
