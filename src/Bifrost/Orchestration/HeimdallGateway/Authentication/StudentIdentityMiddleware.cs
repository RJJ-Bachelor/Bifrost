using Shared.Api.Authentication;
using CookieHeaderValue = Microsoft.Net.Http.Headers.CookieHeaderValue;

namespace HeimdallGateway.Authentication;

public sealed class StudentIdentityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, StudentIdentityCookie studentCookie)
    {
        if (context.Request.Path.StartsWithSegments("/api/students"))
        {
            context.Request.Cookies.TryGetValue(StudentCookieHandler.CookieName, out var cookie);
            if (!studentCookie.TryUnprotect(cookie, out _))
            {
                var userId = Guid.NewGuid().ToString("N");
                var protectedIdentity = studentCookie.Protect(userId);
                context.Response.Cookies.Append(StudentCookieHandler.CookieName, protectedIdentity, new CookieOptions
                {
                    Path = "/api/students",
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = context.Request.IsHttps,
                    IsEssential = true,
                    MaxAge = StudentIdentityCookie.Lifetime
                });

                // The first proxied request must already carry the cookie issued in this response.
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
