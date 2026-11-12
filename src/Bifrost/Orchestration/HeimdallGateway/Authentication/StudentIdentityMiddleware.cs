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
                context.Response.OnStarting(() =>
                {
                    // A downstream identity transfer takes precedence over automatic issuance or renewal.
                    if (!context.Response.Headers.SetCookie.Any(header =>
                        header?.StartsWith(StudentCookieHandler.CookieName + "=", StringComparison.Ordinal) == true))
                        studentCookie.AppendProtectedToResponse(context, protectedIdentity);
                    return Task.CompletedTask;
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
