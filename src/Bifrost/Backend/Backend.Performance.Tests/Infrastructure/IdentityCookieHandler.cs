using System.Net;

namespace Backend.Performance.Tests.Infrastructure;

/// <summary>Keep one identity's cookies while sharing an HTTP connection pool with other identities.</summary>
internal sealed class IdentityCookieHandler(CookieContainer cookies, HttpMessageInvoker transport) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("An absolute request URI is required.");
        request.Headers.Remove("Cookie");
        var header = cookies.GetCookieHeader(uri);
        if (header.Length != 0) request.Headers.TryAddWithoutValidation("Cookie", header);

        var response = await transport.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
            foreach (var value in values)
                try { cookies.SetCookies(uri, value); }
                catch (CookieException) { /* Ignore malformed server cookies, as the normal HTTP handler does. */ }
        return response;
    }

    // The shared transport belongs to the process. Disposing one user must not close other users' pools.
}
