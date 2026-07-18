using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using NetAuth0.Application.Abstractions;
using OtpNet;

namespace NetAuth0.Infrastructure.Security;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class BcryptPasswordHasher : IPasswordHasher
{
    public string Hash(string secret) => BCrypt.Net.BCrypt.EnhancedHashPassword(secret, workFactor: 12);
    public bool Verify(string secret, string hash) => BCrypt.Net.BCrypt.EnhancedVerify(secret, hash);
}

public sealed class SecretGenerator : ISecretGenerator
{
    public string UrlSafe(int byteCount = 32) => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));

    public string Sha256Base64Url(string value)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return WebEncoders.Base64UrlEncode(bytes);
    }

    public bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = System.Text.Encoding.UTF8.GetBytes(left);
        var rightBytes = System.Text.Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

public sealed class TotpService : ITotpService
{
    public string CreateSecret() => Base32Encoding.ToString(RandomNumberGenerator.GetBytes(20));

    public string BuildOtpAuthUri(string issuer, string accountName, string secret)
    {
        return $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits=6&period=30";
    }

    public bool Verify(string secret, string code)
    {
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        return totp.VerifyTotp(code, out _, VerificationWindow.RfcSpecifiedNetworkDelay);
    }
}
