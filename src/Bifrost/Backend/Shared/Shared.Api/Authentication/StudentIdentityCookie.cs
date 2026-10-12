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

    public bool TryUnprotect(string? cookie, out string? userId)
    {
        userId = null;
        if (string.IsNullOrWhiteSpace(cookie)) return false;

        try
        {
            var candidate = protector.Unprotect(cookie);
            if (!Guid.TryParseExact(candidate, "N", out _)) return false;
            userId = candidate;
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return false;
        }
    }
}
