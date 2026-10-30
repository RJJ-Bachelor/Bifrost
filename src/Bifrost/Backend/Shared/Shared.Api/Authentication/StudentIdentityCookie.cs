using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Shared.Api.Authentication;

public sealed class StudentIdentityCookie(IOptionsMonitor<StudentCookieOptions> options)
{
    public const string ProtectionPurpose = "Bifrost.StudentIdentity.v1";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    private readonly ITimeLimitedDataProtector protector = options.Get(StudentCookieHandler.SchemeName)
        .DataProtectionProvider!.CreateProtector(ProtectionPurpose).ToTimeLimitedDataProtector();

    public string Protect(string userId) => protector.Protect(userId, Lifetime);

    public bool TryUnprotect(string? cookie, out string? userId) => TryUnprotect(cookie, out userId, out _);

    public bool TryUnprotect(string? cookie, out string? userId, out DateTimeOffset expiresAt)
    {
        userId = null;
        expiresAt = default;
        if (string.IsNullOrWhiteSpace(cookie)) return false;

        try
        {
            var candidate = protector.Unprotect(cookie, out var expiration);
            if (!Guid.TryParseExact(candidate, "N", out _)) return false;
            userId = candidate;
            expiresAt = expiration;
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return false;
        }
    }
}
