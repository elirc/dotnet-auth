using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NetAuth0.Application.Abstractions;
using NetAuth0.Application.OAuth;

namespace NetAuth0.Api.Controllers;

[ApiController]
public sealed class OAuthController(IOAuthService oauth, RequestContext requestContext) : ControllerBase
{
    [HttpGet("/connect/authorize")]
    [EnableRateLimiting("public-auth")]
    public async Task<IActionResult> Authorize(
        [FromQuery(Name = "client_id")] string clientId,
        [FromQuery(Name = "redirect_uri")] string redirectUri,
        [FromQuery(Name = "response_type")] string responseType,
        [FromQuery] string scope,
        [FromQuery] string state,
        [FromQuery(Name = "code_challenge")] string codeChallenge,
        [FromQuery(Name = "code_challenge_method")] string codeChallengeMethod,
        [FromQuery] string? nonce,
        [FromQuery(Name = "user_id")] Guid userId,
        CancellationToken cancellationToken)
    {
        var redirect = await oauth.CreateAuthorizationCodeAsync(
            new AuthorizeRequest(clientId, redirectUri, responseType, scope, state, codeChallenge, codeChallengeMethod, nonce),
            userId,
            requestContext,
            cancellationToken);

        return Redirect(redirect.RedirectUri);
    }

    [HttpPost("/connect/token")]
    [Consumes("application/x-www-form-urlencoded")]
    [EnableRateLimiting("public-auth")]
    public async Task<IActionResult> Token([FromForm] TokenForm form, CancellationToken cancellationToken)
    {
        var (clientId, clientSecret) = ReadClientCredentials(form);
        var response = await oauth.ExchangeTokenAsync(new TokenRequest(
            form.GrantType,
            form.Code,
            form.RedirectUri,
            clientId,
            clientSecret,
            form.CodeVerifier,
            form.RefreshToken,
            form.Scope), requestContext, cancellationToken);

        return Ok(new
        {
            access_token = response.AccessToken,
            token_type = response.TokenType,
            expires_in = response.ExpiresIn,
            id_token = response.IdToken,
            refresh_token = response.RefreshToken,
            scope = response.Scope
        });
    }

    private (string? ClientId, string? ClientSecret) ReadClientCredentials(TokenForm form)
    {
        if (Request.Headers.Authorization.ToString().StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            var encoded = Request.Headers.Authorization.ToString()["Basic ".Length..].Trim();
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var parts = decoded.Split(':', 2);
            return (Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null);
        }

        return (form.ClientId, form.ClientSecret);
    }
}

public sealed class TokenForm
{
    [FromForm(Name = "grant_type")]
    public string GrantType { get; set; } = string.Empty;
    [FromForm(Name = "code")]
    public string? Code { get; set; }
    [FromForm(Name = "redirect_uri")]
    public string? RedirectUri { get; set; }
    [FromForm(Name = "client_id")]
    public string? ClientId { get; set; }
    [FromForm(Name = "client_secret")]
    public string? ClientSecret { get; set; }
    [FromForm(Name = "code_verifier")]
    public string? CodeVerifier { get; set; }
    [FromForm(Name = "refresh_token")]
    public string? RefreshToken { get; set; }
    [FromForm(Name = "scope")]
    public string? Scope { get; set; }
}
