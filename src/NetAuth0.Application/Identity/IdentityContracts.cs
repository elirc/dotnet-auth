namespace NetAuth0.Application.Identity;

public sealed record SignupRequest(string TenantSlug, string Email, string DisplayName, string Password);
public sealed record LoginRequest(string TenantSlug, string Email, string Password, string? TotpCode);
public sealed record LoginResponse(Guid UserId, Guid TenantId, string Email, string DisplayName, bool RequiresMfa, string? LoginTicket, string? AccessToken, string? IdToken);
public sealed record MfaEnrollmentResponse(Guid EnrollmentId, string Secret, string OtpAuthUri, IReadOnlyList<string> RecoveryCodes);
public sealed record VerifyMfaRequest(Guid EnrollmentId, string Code);
