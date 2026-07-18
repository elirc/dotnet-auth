namespace NetAuth0.Application.OAuth;

public sealed record AuthorizeRequest(
    string ClientId,
    string RedirectUri,
    string ResponseType,
    string Scope,
    string State,
    string CodeChallenge,
    string CodeChallengeMethod,
    string? Nonce);

public sealed record TokenRequest(
    string GrantType,
    string? Code,
    string? RedirectUri,
    string? ClientId,
    string? ClientSecret,
    string? CodeVerifier,
    string? RefreshToken,
    string? Scope);

public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string? IdToken,
    string? RefreshToken,
    string? Scope);

public sealed record AuthorizationRedirect(string RedirectUri);
public sealed record OAuthError(string Error, string ErrorDescription);
public sealed record SessionDto(Guid Id, Guid FamilyId, string ClientName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt);
