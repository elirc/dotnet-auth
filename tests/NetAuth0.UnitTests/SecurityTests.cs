using NetAuth0.Infrastructure.Security;
using Xunit;

namespace NetAuth0.UnitTests;

public sealed class SecurityTests
{
    [Fact]
    public void Sha256Base64Url_IsStableForPkceVerification()
    {
        var generator = new SecretGenerator();
        var verifier = "correct-horse-battery-staple";

        var challenge = generator.Sha256Base64Url(verifier);

        Assert.True(generator.FixedTimeEquals(challenge, generator.Sha256Base64Url(verifier)));
        Assert.False(generator.FixedTimeEquals(challenge, generator.Sha256Base64Url("wrong")));
    }
}
