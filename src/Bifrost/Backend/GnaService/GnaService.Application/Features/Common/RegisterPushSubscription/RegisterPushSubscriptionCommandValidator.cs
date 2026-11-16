using System.Security.Cryptography;
using FluentValidation;

namespace GnaService.Application.Features.Common.RegisterPushSubscription;

public sealed class RegisterPushSubscriptionCommandValidator : AbstractValidator<RegisterPushSubscriptionCommand>
{
    public RegisterPushSubscriptionCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Endpoint).Must(IsHttpsEndpoint).WithMessage("An HTTPS endpoint from a supported push service is required.");
        RuleFor(command => command.P256dh).Must(IsPublicKey).WithMessage("A valid P-256 push public key is required.");
        RuleFor(command => command.Auth).Must(value => Decode(value, 16) is not null)
            .WithMessage("A 16-byte base64url push authentication secret is required.");
    }

    internal static bool IsHttpsEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > 2048 ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        // Push URLs are browser supplied. Restrict outbound requests to the standard browser push services.
        string[] hosts = ["fcm.googleapis.com", "push.services.mozilla.com", "push.apple.com", "notify.windows.com"];
        return hosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPublicKey(string? value)
    {
        var bytes = Decode(value, 65);
        if (bytes is null || bytes[0] != 4) return false;
        try
        {
            using var key = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = bytes[1..33], Y = bytes[33..65] }
            });
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException or PlatformNotSupportedException) { return false; }
    }

    private static byte[]? Decode(string? value, int length)
    {
        if (string.IsNullOrEmpty(value) || value.Length != (length * 8 + 5) / 6 ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')) return null;
        try
        {
            var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
            return bytes.Length == length ? bytes : null;
        }
        catch (FormatException) { return null; }
    }
}
