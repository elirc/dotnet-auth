using System.Security.Claims;
using NetAuth0.Domain.Identity;

namespace NetAuth0.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IPasswordHasher
{
    string Hash(string secret);
    bool Verify(string secret, string hash);
}

public interface ISecretGenerator
{
    string UrlSafe(int byteCount = 32);
    string Sha256Base64Url(string value);
    bool FixedTimeEquals(string left, string right);
}

public interface ITokenIssuer
{
    Task<IssuedJwtPair> IssueUserTokensAsync(User user, ClientApplication application, string scope, string? nonce, CancellationToken cancellationToken);
    Task<string> IssueClientAccessTokenAsync(ClientApplication application, IEnumerable<string> permissions, CancellationToken cancellationToken);
    Task<object> GetJwksAsync(CancellationToken cancellationToken);
}

public interface ITotpService
{
    string CreateSecret();
    string BuildOtpAuthUri(string issuer, string accountName, string secret);
    bool Verify(string secret, string code);
}

public sealed record IssuedJwtPair(string AccessToken, string IdToken, int ExpiresIn);
public sealed record RequestContext(string? IpAddress, string? UserAgent, ClaimsPrincipal? Principal);
