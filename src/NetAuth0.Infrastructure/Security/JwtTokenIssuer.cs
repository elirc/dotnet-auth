using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NetAuth0.Application.Abstractions;
using NetAuth0.Domain.Identity;

namespace NetAuth0.Infrastructure.Security;

public sealed class JwtTokenIssuer(IAppDbContext db, IClock clock, IConfiguration configuration) : ITokenIssuer
{
    private readonly string _issuer = configuration["Auth:Issuer"] ?? "https://localhost:5001";

    public async Task<IssuedJwtPair> IssueUserTokensAsync(User user, ClientApplication application, string scope, string? nonce, CancellationToken cancellationToken)
    {
        var permissions = await db.UserRoles
            .Where(x => x.UserId == user.Id && x.Role.ClientApplicationId == application.Id)
            .SelectMany(x => x.Role.Permissions.Select(p => p.Permission.Name))
            .Distinct()
            .ToListAsync(cancellationToken);

        var now = clock.UtcNow;
        var accessClaims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("tenant_id", user.TenantId.ToString()),
            new("client_id", application.ClientId),
            new("scope", scope),
            new("typ", "access")
        };
        accessClaims.AddRange(permissions.Select(x => new Claim("permissions", x)));

        var idClaims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("name", user.DisplayName),
            new("tenant_id", user.TenantId.ToString()),
            new("client_id", application.ClientId),
            new("typ", "id")
        };
        if (!string.IsNullOrWhiteSpace(nonce))
        {
            idClaims.Add(new Claim(JwtRegisteredClaimNames.Nonce, nonce));
        }

        var accessToken = await CreateJwtAsync(application.ClientId, accessClaims, now.AddMinutes(application.AccessTokenLifetimeMinutes), cancellationToken);
        var idToken = await CreateJwtAsync(application.ClientId, idClaims, now.AddMinutes(application.IdTokenLifetimeMinutes), cancellationToken);
        return new IssuedJwtPair(accessToken, idToken, application.AccessTokenLifetimeMinutes * 60);
    }

    public async Task<string> IssueClientAccessTokenAsync(ClientApplication application, IEnumerable<string> permissions, CancellationToken cancellationToken)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, application.ClientId),
            new("tenant_id", application.TenantId.ToString()),
            new("client_id", application.ClientId),
            new("typ", "access")
        };
        claims.AddRange(permissions.Select(x => new Claim("permissions", x)));
        return await CreateJwtAsync(application.ClientId, claims, clock.UtcNow.AddMinutes(application.AccessTokenLifetimeMinutes), cancellationToken);
    }

    public async Task<object> GetJwksAsync(CancellationToken cancellationToken)
    {
        var keys = await db.SigningKeys
            .Where(x => x.RetiredAt == null || x.RetiredAt > clock.UtcNow.AddDays(-7))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var jwks = keys.Select(key =>
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(key.PrivateKeyPem);
            var parameters = rsa.ExportParameters(false);
            return new
            {
                kty = "RSA",
                use = "sig",
                kid = key.KeyId,
                alg = SecurityAlgorithms.RsaSha256,
                n = WebEncoders.Base64UrlEncode(parameters.Modulus!),
                e = WebEncoders.Base64UrlEncode(parameters.Exponent!)
            };
        });

        return new { keys = jwks };
    }

    private async Task<string> CreateJwtAsync(string audience, IEnumerable<Claim> claims, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var key = await GetCurrentSigningKeyAsync(cancellationToken);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(key.PrivateKeyPem);
        var securityKey = new RsaSecurityKey(rsa) { KeyId = key.KeyId };
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: audience,
            claims: claims,
            notBefore: clock.UtcNow.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<SigningKey> GetCurrentSigningKeyAsync(CancellationToken cancellationToken)
    {
        var current = await db.SigningKeys
            .Where(x => x.RetiredAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is not null)
        {
            return current;
        }

        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportRSAPrivateKeyPem();
        var key = new SigningKey($"kid_{Guid.NewGuid():N}", pem, clock.UtcNow);
        db.SigningKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);
        return key;
    }
}
