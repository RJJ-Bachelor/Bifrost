using System.Security.Cryptography;
using GnaService.Infrastructure.Push;
using Microsoft.AspNetCore.WebUtilities;

namespace GnaService.Test;

public sealed class WebPushOptionsTests
{
    [Fact]
    public void DisabledPushNeedsNoKeys()
    {
        Assert.True(new WebPushOptions().IsValid());
    }

    [Theory]
    [InlineData("mailto:push@example.test")]
    [InlineData("https://example.test/contact")]
    public void MatchingP256KeysAndContactSubjectAreValid(string subject)
    {
        var options = ValidOptions();
        options.Subject = subject;

        Assert.True(options.IsValid());
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("invalid!", "invalid!")]
    [InlineData("BA", "AQ")]
    public void EnabledPushRejectsMissingOrMalformedKeys(string publicKey, string privateKey)
    {
        var options = ValidOptions();
        options.PublicKey = publicKey;
        options.PrivateKey = privateKey;

        Assert.False(options.IsValid());
    }

    [Fact]
    public void EnabledPushRejectsDifferentPublicAndPrivateKeyPairs()
    {
        var options = ValidOptions();
        options.PrivateKey = ValidOptions().PrivateKey;

        Assert.False(options.IsValid());
    }

    [Fact]
    public void EnabledPushRejectsAnInvalidCurvePoint()
    {
        var options = ValidOptions();
        var invalidPoint = new byte[65];
        invalidPoint[0] = 4;
        options.PublicKey = WebEncoders.Base64UrlEncode(invalidPoint);

        Assert.False(options.IsValid());
    }

    [Theory]
    [InlineData("")]
    [InlineData("push@example.test")]
    [InlineData("http://example.test/contact")]
    [InlineData("ftp://example.test/contact")]
    public void EnabledPushRejectsInvalidContactSubject(string subject)
    {
        var options = ValidOptions();
        options.Subject = subject;

        Assert.False(options.IsValid());
    }

    private static WebPushOptions ValidOptions()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        return new WebPushOptions
        {
            Enabled = true,
            Subject = "mailto:push@example.test",
            PublicKey = WebEncoders.Base64UrlEncode([4, .. parameters.Q.X!, .. parameters.Q.Y!]),
            PrivateKey = WebEncoders.Base64UrlEncode(parameters.D!)
        };
    }
}
