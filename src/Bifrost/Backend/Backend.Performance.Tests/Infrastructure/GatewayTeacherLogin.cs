using System.Net;
using Backend.Performance.Tests.Reporting;
using Microsoft.Playwright;

namespace Backend.Performance.Tests.Infrastructure;

/// <summary>
/// Uses Heimdall's unchanged /account/login authorization-code+PKCE flow. Its protected teacher
/// cookie contains the saved Var JWT, which Heimdall forwards to Eir/Gna after authentication.
/// </summary>
public static class GatewayTeacherLogin
{
    public static async Task<GatewayClient[]> LoginAllAsync(Uri gateway, MetricsCollector metrics,
        TimeSpan requestTimeout, CancellationToken cancellationToken = default)
    {
        var credentials = TeacherCredentials.FromEnvironment();
        using var playwright = await Playwright.CreateAsync().WaitAsync(cancellationToken);
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        }).WaitAsync(cancellationToken);
        List<GatewayClient> clients = [];
        var slot = 0;
        var stage = "browser initialization";
        int? lastHttpStatus = null;
        IPage? currentPage = null;
        try
        {
            foreach (var credential in credentials)
            {
                slot = credential.Number;
                lastHttpStatus = null;
                stage = "open gateway login";
                await using var context = await browser.NewContextAsync().WaitAsync(cancellationToken);
                var page = await context.NewPageAsync().WaitAsync(cancellationToken);
                currentPage = page;
                page.SetDefaultTimeout((float)requestTimeout.TotalMilliseconds);
                var loginPage = await page.GotoAsync(new Uri(gateway, "/account/login?returnUrl=%2Fapi%2Fteachers%2Fme").AbsoluteUri,
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).WaitAsync(cancellationToken);
                lastHttpStatus = loginPage?.Status;

                // These exact selectors come from VarService/Pages/Account/Login/Index.cshtml.
                stage = "fill Var login form";
                await page.Locator("input[name='Input.Username']").FillAsync(credential.Username).WaitAsync(cancellationToken);
                await page.Locator("input[name='Input.Password']").FillAsync(credential.Password).WaitAsync(cancellationToken);
                stage = "submit Var login form";
                var identityLogin = new Uri(page.Url);
                var loginResponse = await page.RunAndWaitForResponseAsync(
                    () => page.Locator("button[name='Input.Button'][value='login']").ClickAsync(),
                    response => response.Request.Method == "POST" &&
                        Uri.TryCreate(response.Url, UriKind.Absolute, out var responseUri) &&
                        responseUri.GetLeftPart(UriPartial.Path) == identityLogin.GetLeftPart(UriPartial.Path))
                    .WaitAsync(cancellationToken);
                lastHttpStatus = loginResponse.Status;
                if (loginResponse.Status >= 400)
                    throw new TeacherLoginRejectedException($"VarService login returned HTTP {loginResponse.Status}; inspect its logs before retrying.");
                stage = "wait for gateway callback";
                // /teachers/me returns JSON. Chromium's JSON viewer can leave DOM/load events
                // pending after the redirect has committed; validate identity via HTTP below.
                if (!IsTeacherLocation(page.Url, gateway))
                {
                    // A rejected password renders the login page again with HTTP 200. It is
                    // not an OIDC callback timeout and must not trigger automatic retries,
                    // since Var uses lockoutOnFailure:true. Observe either outcome directly.
                    try
                    {
                        var state = await page.WaitForFunctionAsync("""
                            ({ origin }) =>
                                (location.origin === origin && location.pathname.replace(/\/+$/, '') === '/api/teachers/me') ||
                                !!document.querySelector('.validation-summary-errors li')
                            """, new { origin = gateway.GetLeftPart(UriPartial.Authority) }).WaitAsync(cancellationToken);
                        await state.DisposeAsync();
                    }
                    catch (TimeoutException) when (IsTeacherLocation(page.Url, gateway)) { }

                    if (!IsTeacherLocation(page.Url, gateway))
                    {
                        var credentialsRejected = await page.EvaluateAsync<bool>("""
                            () => Array.from(document.querySelectorAll('.validation-summary-errors li'))
                                .some(item => item.textContent.trim() === 'Invalid username or password')
                            """).WaitAsync(cancellationToken);
                        throw new TeacherLoginRejectedException(credentialsRejected
                            ? $"VarService rejected the credentials for teacher slot {slot}. Check BIFROST_PERF_TEACHER_{slot}_USERNAME and _PASSWORD. The account may also be temporarily locked; the login page does not distinguish these cases. See the script README for Development seed login. No retry was attempted."
                            : "VarService rejected the login form. Check its validation errors and logs; no form contents were saved.");
                    }
                }

                stage = "transfer issued teacher cookie";
                var cookies = new CookieContainer();
                foreach (var cookie in await context.CookiesAsync([gateway.AbsoluteUri]).WaitAsync(cancellationToken))
                {
                    if (cookie.Name != "bifrost-teacher" && !cookie.Name.StartsWith("bifrost-teacherC", StringComparison.Ordinal))
                        continue;
                    var transferred = new System.Net.Cookie(cookie.Name, cookie.Value, cookie.Path)
                    {
                        HttpOnly = cookie.HttpOnly,
                        Secure = cookie.Secure
                    };
                    if (cookie.Expires > 0)
                        transferred.Expires = DateTimeOffset.FromUnixTimeSeconds((long)cookie.Expires).UtcDateTime;
                    cookies.Add(gateway, transferred);
                }
                var client = new GatewayClient(gateway, metrics, requestTimeout, cookies);
                clients.Add(client);
                // Server-side authentication validates the saved JWT, role and scope. Browser
                // HTML/redirect success alone is never treated as a successful teacher login.
                stage = "validate teacher through gateway API";
                await client.AuthenticateTeacherAsync(cancellationToken);
                Console.WriteLine($"Teacher slot {slot}: gateway identity and role validated.");
            }
            if (clients.Select(client => client.UserId).Distinct(StringComparer.Ordinal).Count() != 30)
                throw new InvalidOperationException("OIDC must authenticate thirty separate teacher users.");
            return clients.ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            foreach (var client in clients) client.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            foreach (var client in clients) client.Dispose();
            // Playwright exceptions may include token-bearing callback URLs and credential field
            // input. Keep caller reports safe; the identity provider's own logs retain diagnostics.
            var status = exception is HttpRequestException http ? http.StatusCode?.ToString() : lastHttpStatus?.ToString();
            var finalRoute = Uri.TryCreate(currentPage?.Url, UriKind.Absolute, out var finalUri)
                ? finalUri.GetLeftPart(UriPartial.Path) : "unknown";
            var guidance = exception is TeacherLoginRejectedException rejected ? rejected.Message
                : "Check Var/gateway logs, credentials, browser installation and registered redirect URLs.";
            throw new InvalidOperationException($"Teacher OIDC login failed for slot {slot} at {stage} ({exception.GetType().Name}, HTTP {status ?? "n/a"}, route {finalRoute}). {guidance} No credentials or callback query strings were persisted.");
        }
    }

    private sealed class TeacherLoginRejectedException(string message) : Exception(message);

    private static bool IsTeacherLocation(string url, Uri gateway) =>
        Uri.TryCreate(url, UriKind.Absolute, out var current) &&
        current.Scheme.Equals(gateway.Scheme, StringComparison.OrdinalIgnoreCase) &&
        current.Host.Equals(gateway.Host, StringComparison.OrdinalIgnoreCase) && current.Port == gateway.Port &&
        current.AbsolutePath.TrimEnd('/') == "/api/teachers/me";
}
