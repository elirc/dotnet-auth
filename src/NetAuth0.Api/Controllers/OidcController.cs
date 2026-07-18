using Microsoft.AspNetCore.Mvc;
using NetAuth0.Application.Abstractions;

namespace NetAuth0.Api.Controllers;

[ApiController]
public sealed class OidcController(ITokenIssuer tokenIssuer, IConfiguration configuration) : ControllerBase
{
    [HttpGet("/.well-known/openid-configuration")]
    public IActionResult Discovery()
    {
        var issuer = configuration["Auth:Issuer"] ?? $"{Request.Scheme}://{Request.Host}";
        return Ok(new
        {
            issuer,
            authorization_endpoint = $"{issuer}/connect/authorize",
            token_endpoint = $"{issuer}/connect/token",
            jwks_uri = $"{issuer}/.well-known/jwks.json",
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "client_credentials", "refresh_token" },
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { "RS256" },
            code_challenge_methods_supported = new[] { "S256", "plain" },
            scopes_supported = new[] { "openid", "profile", "email", "offline_access" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_post", "client_secret_basic", "none" }
        });
    }

    [HttpGet("/.well-known/jwks.json")]
    public async Task<IActionResult> Jwks(CancellationToken cancellationToken)
    {
        return Ok(await tokenIssuer.GetJwksAsync(cancellationToken));
    }
}
