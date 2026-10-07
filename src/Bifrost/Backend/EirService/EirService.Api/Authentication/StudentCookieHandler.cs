using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace EirService.Api.Authentication;

public sealed class StudentCookieHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IDataProtectionProvider dataProtection)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "StudentCookie";
    public const string CookieName = "bifrost-anonymous-user";
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);
    private readonly ITimeLimitedDataProtector protector = dataProtection
        .CreateProtector("Bifrost.StudentIdentity.v1").ToTimeLimitedDataProtector();

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? userId = null;
        if (Request.Cookies.TryGetValue(CookieName, out var cookie))
        {
            try
            {
                var candidate = protector.Unprotect(cookie);
                if (Guid.TryParseExact(candidate, "N", out _)) userId = candidate;
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                // Missing, expired, legacy or altered cookies start a new anonymous identity.
            }
        }

        if (userId is null)
        {
            userId = Guid.NewGuid().ToString("N");
            Response.Cookies.Append(CookieName, protector.Protect(userId, Lifetime), new CookieOptions
            {
                Path = "/api/students",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                IsEssential = true,
                MaxAge = Lifetime
            });
        }

        var identity = new ClaimsIdentity(
            new[] { new Claim("sub", userId), new Claim("role", "Student") },
            SchemeName, "sub", "role");
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
