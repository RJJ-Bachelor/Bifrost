using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shared.Api.Authentication;

public sealed class StudentCookieHandler(
    IOptionsMonitor<StudentCookieOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    StudentIdentityCookie studentCookie)
    : AuthenticationHandler<StudentCookieOptions>(options, logger, encoder)
{
    public const string SchemeName = "StudentCookie";
    public const string CookieName = "bifrost-anonymous-user";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(CookieName, out var cookie))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!studentCookie.TryUnprotect(cookie, out var userId))
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired student cookie."));

        var identity = new ClaimsIdentity(
            [new Claim("sub", userId!), new Claim("role", "Student")],
            SchemeName, "sub", "role");
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
