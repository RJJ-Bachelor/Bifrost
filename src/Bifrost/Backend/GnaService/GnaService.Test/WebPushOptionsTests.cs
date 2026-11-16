using System.Security.Cryptography;
using GnaService.Infrastructure.Push;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;

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

    [Fact]
    public void PrivateKeyAloneDerivesTheMatchingPublicKey()
    {
        var options = ValidOptions();
        var expectedPublicKey = options.PublicKey;
        options.PublicKey = string.Empty;

        Assert.True(options.IsValid());
        Assert.Equal(expectedPublicKey, options.PublicKey);
    }

    [Fact]
    public void ConfigurationBindingSupportsPrivateKeyAlone()
    {
        var expected = ValidOptions();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Enabled"] = "true",
            ["Subject"] = expected.Subject,
            ["PrivateKey"] = expected.PrivateKey
        }).Build();

        var options = configuration.Get<WebPushOptions>()!;

        Assert.True(options.IsValid());
        Assert.Equal(expected.PublicKey, options.PublicKey);
    }

    [Fact]
    public void ConfigurationBindingIgnoresUnusedPrivateKeyWhenDisabled()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Enabled"] = "false",
            ["PrivateKey"] = "invalid!"
        }).Build();

        var options = configuration.Get<WebPushOptions>()!;

        Assert.True(options.IsValid());
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid!")]
    [InlineData("AQ")]
    public void PrivateKeyAloneRejectsMissingOrMalformedKeys(string privateKey)
    {
        var options = ValidOptions();
        options.PublicKey = string.Empty;
        options.PrivateKey = privateKey;

        Assert.False(options.IsValid());
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
