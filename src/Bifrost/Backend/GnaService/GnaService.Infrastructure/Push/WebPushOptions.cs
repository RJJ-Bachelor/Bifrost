using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace GnaService.Infrastructure.Push;

public sealed class WebPushOptions
{
    public const string SectionName = "Push";

    public bool Enabled { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;

    public bool IsValid()
    {
        if (!Enabled) return true;
        if (!Uri.TryCreate(Subject, UriKind.Absolute, out var subject)
            || subject.Scheme is not ("mailto" or "https")) return false;

        try
        {
            var publicKey = WebEncoders.Base64UrlDecode(PublicKey);
            var privateKey = WebEncoders.Base64UrlDecode(PrivateKey);
            if (publicKey.Length != 65 || publicKey[0] != 4 || privateKey.Length != 32) return false;
            var publicParameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] }
            };
            using var verifier = ECDsa.Create(publicParameters);
            var privateParameters = publicParameters;
            privateParameters.D = privateKey;
            using var signer = ECDsa.Create(privateParameters);
            // Importing alone does not verify that the keys match on every platform.
            byte[] probe = [1, 2, 3];
            return verifier.VerifyData(probe, signer.SignData(probe, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or CryptographicException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
