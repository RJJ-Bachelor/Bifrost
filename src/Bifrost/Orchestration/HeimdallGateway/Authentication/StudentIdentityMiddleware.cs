using Shared.Api.Authentication;
using CookieHeaderValue = Microsoft.Net.Http.Headers.CookieHeaderValue;

namespace HeimdallGateway.Authentication;

public sealed class StudentIdentityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, StudentIdentityCookie studentCookie, TimeProvider clock)
    {
        if (context.Request.Path.StartsWithSegments("/api/students"))
        {
            context.Request.Cookies.TryGetValue(StudentCookieHandler.CookieName, out var cookie);
            var hasValidIdentity = studentCookie.TryUnprotect(cookie, out var userId, out var expiresAt);
            if (!hasValidIdentity || expiresAt - clock.GetUtcNow() <= StudentIdentityCookie.Lifetime / 2)
            {
                var protectedIdentity = studentCookie.Protect(hasValidIdentity ? userId! : Guid.NewGuid().ToString("N"));
                context.Response.Cookies.Append(StudentCookieHandler.CookieName, protectedIdentity, new CookieOptions
                {
                    Path = "/api/students",
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = context.Request.IsHttps,
                    IsEssential = true,
                    MaxAge = StudentIdentityCookie.Lifetime
                });

                // Forward the newly issued or renewed cookie on this request too.
                CookieHeaderValue.TryParseList(context.Request.Headers.Cookie.OfType<string>().ToArray(), out var requestCookies);
                var otherCookies = (requestCookies ?? [])
                    .Where(cookieHeader => cookieHeader.Name != StudentCookieHandler.CookieName)
                    .Select(cookieHeader => cookieHeader.ToString());
                context.Request.Headers.Cookie = string.Join("; ",
                    otherCookies.Append($"{StudentCookieHandler.CookieName}={protectedIdentity}"));
            }
        }

        await next(context);
    }
}
