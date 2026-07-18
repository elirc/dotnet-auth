using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetAuth0.Application.Abstractions;
using NetAuth0.Application.Admin;
using NetAuth0.Application.Audit;
using NetAuth0.Application.Identity;
using NetAuth0.Application.OAuth;
using NetAuth0.Infrastructure.Persistence;
using NetAuth0.Infrastructure.Security;

namespace NetAuth0.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<DbSeeder>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ISecretGenerator, SecretGenerator>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddScoped<ITokenIssuer, JwtTokenIssuer>();
        services.AddScoped<IKeyManagementService, KeyManagementService>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IOAuthService, OAuthService>();
        services.AddScoped<IAdminService, AdminService>();

        return services;
    }
}
