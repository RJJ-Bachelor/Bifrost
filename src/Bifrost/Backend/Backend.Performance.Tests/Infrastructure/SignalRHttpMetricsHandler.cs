using Backend.Performance.Tests.Reporting;

namespace Backend.Performance.Tests.Infrastructure;

/// <summary>
/// Counts SignalR negotiation/fallback HTTP requests without reading streaming response bodies.
/// The wrapped handler keeps SignalR's normal cookies, authentication and transport behavior.
/// </summary>
public sealed class SignalRHttpMetricsHandler(
    HttpMessageHandler inner, MetricsCollector metrics, string actor,
    Func<bool>? callerCancellationRequested = null) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var started = metrics.Capture();
        var operation = $"SignalR {request.Method} {request.RequestUri?.AbsolutePath ?? "/"}";
        int? status = null;
        var success = false;
        string? error = null;
        try
        {
            // DelegatingHandler completion ends at response headers. SSE/long-poll response bodies
            // remain owned by SignalR and must never be drained here for timing or inspection.
            var response = await base.SendAsync(request, cancellationToken);
            status = (int)response.StatusCode;
            success = response.IsSuccessStatusCode;
            if (!success) error = $"HttpStatus{status}";
            return response;
        }
        catch (OperationCanceledException) when (callerCancellationRequested?.Invoke() == true)
        {
            error = "CallerCancellation";
            throw;
        }
        catch (Exception exception)
        {
            // A cancelled HttpClient-linked token alone cannot identify caller cancellation:
            // HttpClient timeouts cancel that token too. Absent an explicit caller signal it fails.
            error = exception.GetType().Name;
            throw;
        }
        finally
        {
            // No query, request/response body, cookie, token or authorization header is persisted.
            metrics.RecordOperation(error == "CallerCancellation" ? "cancelled" : "http",
                operation, started, success, status, actor, error: error);
        }
    }
}
