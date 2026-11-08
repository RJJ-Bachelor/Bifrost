using System.Globalization;
using System.Net;
using System.Text;

namespace Backend.Performance.Tests.Reporting;

/// <summary>A portable report with embedded SVGs; no network access or chart library is required.</summary>
internal static class HtmlReport
{
    private static readonly string[] Colors = ["#1667ba", "#c13b25", "#2d8451", "#9949ac", "#b77808", "#326f75", "#7e593e"];
    private sealed record Series(string Name, IReadOnlyList<(double X, double? Y)> Points);

    public static string Render(DateTimeOffset startedUtc, double durationSeconds, int finalConnections,
        IReadOnlyList<LoadSample> samples, IReadOnlyList<OperationMeasurement> operations, IReadOnlyList<OperationSummary> summaries,
        RunStatus status, IReadOnlyList<ConnectionMeasurement>? connections = null,
        IReadOnlyList<RequirementResult>? requirements = null)
    {
        var html = new StringBuilder("<!doctype html><html lang=\"da\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Backend performance report</title><style>body{font:15px system-ui,sans-serif;background:#f3f5f8;color:#192333;max-width:1200px;margin:30px auto;padding:0 18px}h1{font-size:26px}h2{font-size:19px}section{background:white;border:1px solid #dbe0e8;border-radius:10px;padding:18px;margin:18px 0}svg{display:block;width:100%;height:auto}table{border-collapse:collapse;font-size:12px;width:100%}td,th{text-align:right;padding:7px;border-bottom:1px solid #e0e5eb}td:nth-child(-n+3),th:nth-child(-n+3){text-align:left}.scroll{overflow:auto}.muted{color:#586575}.legend{display:flex;gap:16px;flex-wrap:wrap;font-size:12px}.key{display:inline-block;width:14px;height:3px;vertical-align:middle;margin-right:5px}a{color:#1667ba}</style><h1>Backend performance report</h1>");
        var title = status.Outcome switch
        {
            "failed" => "Run failed",
            "cancelled" => "Run cancelled",
            "preflight-only" => "Student preflight only",
            "teacher-preflight-only" => "Student and teacher preflight only",
            "completed" when operations.Any(o => !o.Success && o.Category != "cancelled") => "Load run completed with observed failures",
            "completed" => "Load run completed",
            _ => "Run outcome unavailable"
        };
        html.Append("<section style=\"border-left:5px solid ")
            .Append(status.Outcome == "failed" ? "#c13b25" : "#1667ba").Append("\"><h2>")
            .Append(E(title)).Append("</h2><p>Outcome: <strong>").Append(E(status.Outcome)).Append("</strong>.</p>");
        if (!status.MeasurementStarted)
            html.Append("<p><strong>No measured load was recorded.</strong> The load measurement phase did not start. Empty HTTP and connection counts are not evidence of a successful performance test.</p>");
        else if (status.Outcome is "failed" or "cancelled")
            html.Append("<p>Only partial load measurements are available; the configured test profile did not complete.</p>");
        if (!string.IsNullOrWhiteSpace(status.Failure))
            html.Append("<pre style=\"white-space:pre-wrap;overflow-wrap:anywhere\">").Append(E(status.Failure)).Append("</pre>");
        html.Append("</section>");
        if (status.MeasurementStarted)
            html.Append("<p>Measured from ").Append(E(startedUtc.ToString("O"))).Append("; duration ").Append(F(durationSeconds)).Append(" s. Final actual SignalR connections: <strong>").Append(finalConnections).Append("</strong>.</p>");
        html.Append("<p class=\"muted\">Warm-up is excluded. HTTP durations and matching notification durations use one monotonic Stopwatch clock on the generator. Notification failures include explicit timeouts. HTTP throughput counts completed requests. CPU is percent of the machine's logical CPU capacity. Process sampling does not imply server metrics; only configured, readable local PIDs are measured. Missing observations are left blank. Browser feedback is only measured when browser observations exist.</p><p><a href=\"raw.json\">Raw JSON</a> · <a href=\"operations.csv\">Operations</a> · <a href=\"load.csv\">Load</a> · <a href=\"connections.csv\">Connections</a> · <a href=\"resources.csv\">Resources</a> · <a href=\"summary.csv\">Summary</a> · <a href=\"summary.json\">Configuration and summary</a></p>");
        if (!status.MeasurementStarted)
            return html.Append("<section><h2>Measurements unavailable</h2><p>The three requirements cannot be assessed without a measured load run. Resolve the startup failure and start a new run.</p></section></html>").ToString();
        connections ??= [];
        AddRequirements(html, requirements);
        AddDiagnostics(html, operations, connections);
        var phases = samples.GroupBy(s => s.Phase).Select(g => (Phase: g.Key, Start: g.Min(s => s.ElapsedSeconds))).OrderBy(p => p.Start).ToArray();
        Chart(html, "SignalR connections", "connections", durationSeconds, phases,
            new("Target", samples.Select(s => (s.ElapsedSeconds, (double?)s.TargetConnections)).ToArray()),
            new("Actual", samples.Select(s => (s.ElapsedSeconds, (double?)s.ActualConnections)).ToArray()));
        Chart(html, "HTTP throughput and errors", "requests / s", durationSeconds, phases,
            new("Completed HTTP", samples.Select(s => (s.ElapsedSeconds, (double?)s.HttpRequestsPerSecond)).ToArray()),
            new("Failed HTTP", samples.Select(s => (s.ElapsedSeconds, (double?)s.HttpFailuresPerSecond)).ToArray()));
        Series ConnectionSeries(string name, Func<ConnectionMeasurement, bool> include) => new(name,
            samples.Select(s => (s.ElapsedSeconds, (double?)connections.LongCount(c => c.ElapsedSeconds <= s.ElapsedSeconds && include(c)))).ToArray());
        Chart(html, "Planned shutdown, connection loss and recovery", "cumulative events", durationSeconds, phases,
            ConnectionSeries("Planned closes", c => c.Event == "closed" && MetricsCollector.IsPlannedClosureReason(c.Reason)),
            ConnectionSeries("Unexpected closes", c => c.Event == "closed" && !MetricsCollector.IsPlannedClosureReason(c.Reason) && !MetricsCollector.IsUnclassifiedShutdownClosure(c)),
            ConnectionSeries("Shutdown closes, reason unavailable", MetricsCollector.IsUnclassifiedShutdownClosure),
            ConnectionSeries("Reconnecting", c => c.Event == "reconnecting"),
            ConnectionSeries("Recovered", c => c.Event == "reconnected"));
        AddLatencyChart(html, "HTTP latency (one-second completion buckets)", "http", durationSeconds, phases, operations);
        if (requirements is not null)
        {
            AddLatencyChart(html, "K1: HelpRequest registration HTTP", "http", durationSeconds, phases, operations,
                operation: PerformanceRequirements.CreateOperation, limitMs: 1000);
            AddLatencyChart(html, "K2: First request to teacher notification", "notification", durationSeconds, phases, operations,
                successful: true, operation: PerformanceRequirements.QueueOperation, limitMs: 10000);
            AddLatencyChart(html, "K3: Teacher answer to student notification", "notification", durationSeconds, phases, operations,
                successful: true, operation: PerformanceRequirements.AnswerOperation, limitMs: 10000);
        }
        AddLatencyChart(html, "HTTP send to matching SignalR message (successful deliveries)", "notification", durationSeconds, phases, operations, successful: true);
        if (operations.Any(o => o.Category == "notification" && !o.Success))
            AddLatencyChart(html, "Failed notification waits (no matching delivery before the deadline)", "notification", durationSeconds, phases, operations, successful: false);
        if (operations.Any(o => o.Category == "browser"))
            AddLatencyChart(html, "Visible browser feedback", "browser", durationSeconds, phases, operations);
        var processes = samples.SelectMany(s => s.Processes).Select(p => (p.ProcessId, p.ProcessName)).Distinct().ToArray();
        Chart(html, "Available process CPU", "% of machine", durationSeconds, phases,
            processes.Select(p => new Series($"{p.ProcessName} ({p.ProcessId})", samples.Select(s => (s.ElapsedSeconds, s.Processes.FirstOrDefault(m => m.ProcessId == p.ProcessId)?.CpuPercent)).ToArray())).ToArray());
        Chart(html, "Available process working set", "MiB", durationSeconds, phases,
            processes.Select(p => new Series($"{p.ProcessName} ({p.ProcessId})", samples.Select(s => (s.ElapsedSeconds, s.Processes.FirstOrDefault(m => m.ProcessId == p.ProcessId)?.WorkingSetBytes / (double?)(1024 * 1024))).ToArray())).ToArray());
        html.Append("<section><h2>Operation summaries</h2><p class=\"muted\">Nearest-rank percentiles; completed observations include failures. These are diagnostic timings. Only K1 has a one-second limit; K2 and K3 allow ten seconds. Phase is captured when the operation starts.</p><div class=\"scroll\"><table><thead><tr><th>Phase</th><th>Category</th><th>Operation</th><th>Count</th><th>Failures</th><th>p50 ms</th><th>p95 ms</th><th>p99 ms</th><th>Max ms</th></tr></thead><tbody>");
        foreach (var s in summaries)
            html.Append("<tr><td>").Append(E(s.Phase)).Append("</td><td>").Append(E(s.Category)).Append("</td><td>").Append(E(s.Operation)).Append("</td><td>").Append(s.Count).Append("</td><td>").Append(s.Failures).Append("</td><td>").Append(F(s.P50Ms)).Append("</td><td>").Append(F(s.P95Ms)).Append("</td><td>").Append(F(s.P99Ms)).Append("</td><td>").Append(F(s.MaximumMs)).Append("</td></tr>");
        html.Append("</tbody></table></div></section><section><h2>Observed errors</h2><div class=\"scroll\"><table><thead><tr><th>Category</th><th>Operation</th><th>Error / status</th><th>Count</th></tr></thead><tbody>");
        foreach (var group in operations.Where(o => !o.Success).GroupBy(o => (o.Category, o.Operation, o.Error, o.StatusCode)).OrderByDescending(g => g.Count()).Take(100))
            html.Append("<tr><td>").Append(E(group.Key.Category)).Append("</td><td>").Append(E(group.Key.Operation)).Append("</td><td>").Append(E(group.Key.Error ?? group.Key.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "unspecified")).Append("</td><td>").Append(group.Count()).Append("</td></tr>");
        html.Append("</tbody></table></div></section></html>");
        return html.ToString();
    }

    private static void AddRequirements(StringBuilder html, IReadOnlyList<RequirementResult>? requirements)
    {
        if (requirements is null)
        {
            html.Append("<section><h2>Historical measurement rules</h2><p>This run predates the current 1s / 10s / 10s requirements. Its raw observations are preserved; they do not verify all three current requirements. Run a new test.</p></section>");
            return;
        }
        html.Append("<section><h2>Requirements: 1s / 10s / 10s</h2><p>A maximum limit applies to every measured action, not just p95. Exactly one or ten seconds is allowed. Failed operations also fail the requirement. No observations means not measured; cancelled probes or missing queue probes mean incomplete.</p><div class=\"scroll\"><table><thead><tr><th>Requirement</th><th>Measurement</th><th>Limit ms</th><th>N</th><th>Errors</th><th>Over limit</th><th>Max ms</th><th>Cancelled</th><th>Status</th></tr></thead><tbody>");
        foreach (var value in requirements)
            html.Append("<tr><td>").Append(E(value.Id)).Append("</td><td>").Append(E(value.Description))
                .Append("</td><td>").Append(F(value.LimitMs)).Append("</td><td>").Append(value.Measurements)
                .Append("</td><td>").Append(value.Errors).Append("</td><td>").Append(value.OverLimit)
                .Append("</td><td>").Append(value.MaximumMs is { } maximum ? F(maximum) : "—")
                .Append("</td><td>").Append(value.Cancelled).Append("</td><td>").Append(E(value.Status)).Append("</td></tr>");
        html.Append("</tbody></table></div><p>K2 measures the first creation in each fresh lesson queue, ending at the earliest matching notification received by one of that session's three teachers. Later empty-to-nonempty transitions are not covered. K3 probes ordinary digital answers with an exact request ID; canonical FAQ broadcasts and physical start/completion are diagnostics. K2/K3 stop at the SignalR callback; visible rendering is optional diagnostics. Operational HTTP and connection failures remain listed separately.</p></section>");
    }

    private static void AddDiagnostics(StringBuilder html, IReadOnlyList<OperationMeasurement> operations,
        IReadOnlyList<ConnectionMeasurement> connections)
    {
        var diagnostics = MetricsCollector.Diagnose(operations, connections);
        html.Append("<section><h2>Failure diagnostics</h2><p>Connection setup and other HTTP calls have no one-second business limit.</p><div class=\"scroll\"><table><thead><tr><th>Observation</th><th>Count</th><th>Succeeded</th><th>Failed</th></tr></thead><tbody>");
        void Observation(string name, long count, long failures) => html
            .Append("<tr><td>").Append(E(name)).Append("</td><td>").Append(count).Append("</td><td>")
            .Append(count - failures).Append("</td><td>").Append(failures).Append("</td></tr>");
        Observation("HTTP requests", diagnostics.HttpRequests, diagnostics.HttpFailures);
        Observation("SignalR connection setup", diagnostics.ConnectionAttempts, diagnostics.ConnectionFailures);
        Observation("Matching notification measurements", diagnostics.NotificationMeasurements, diagnostics.NotificationFailures);
        html.Append("</tbody></table></div><p>A failed notification wait means no matching message arrived within its deadline; this is not a measured delivery latency. Unmatched messages are retained in notifications.csv.</p><div class=\"scroll\"><table><thead><tr><th>Connection lifecycle</th><th>Count</th></tr></thead><tbody>");
        void Count(string label, long count) => html.Append("<tr><td>").Append(E(label)).Append("</td><td>").Append(count).Append("</td></tr>");
        Count("Explicitly planned closes", diagnostics.PlannedConnectionClosures);
        Count("Closes during shutdown, reason unavailable", diagnostics.UnclassifiedShutdownClosures);
        Count("Unexpected closes", diagnostics.UnexpectedConnectionClosures);
        Count("Reconnecting events (connection lost)", diagnostics.ReconnectingEvents);
        Count("Recovered connections", diagnostics.ReconnectedEvents);
        html.Append("</tbody></table></div><p>Planned closes during ramp-down or cleanup are expected. Older exports may record a close without a reason during shutdown; those observations remain unclassified rather than being assumed to be planned or failed.</p><p><strong>Failed scenario iterations: ")
            .Append(diagnostics.IterationFailures).Append(".</strong> Other failed observations: ").Append(diagnostics.OtherFailedObservations)
            .Append(". Total failed observations: ")
            .Append(operations.LongCount(o => !o.Success && !o.Category.Equals("cancelled", StringComparison.OrdinalIgnoreCase)))
            .Append(".</p><p class=\"muted\">TotalFailures in summary.json counts failed observations, not independent incidents. One notification timeout can also cause one failed student or teacher iteration; these counts must not be added together as independent delivery faults. All original failures, operation summaries and raw records remain available.</p></section>");
    }

    private static void AddLatencyChart(StringBuilder html, string title, string category, double durationSeconds,
        (string Phase, double Start)[] phases, IReadOnlyList<OperationMeasurement> operations, bool? successful = null,
        string? operation = null, double? limitMs = null)
    {
        var buckets = operations.Where(o => o.Category.Equals(category, StringComparison.OrdinalIgnoreCase) &&
                (successful is null || o.Success == successful.Value) && (operation is null || o.Operation == operation))
            .GroupBy(o => Math.Floor(o.ElapsedSeconds + o.DurationMs / 1000)).OrderBy(g => g.Key)
            .Select(g => (Second: g.Key, Values: g.Select(o => o.DurationMs).Order().ToArray())).ToArray();
        DrawChart(html, title, "ms", durationSeconds, phases, limitMs,
            new("p50", buckets.Select(b => (b.Second, (double?)MetricsCollector.Percentile(b.Values, .50))).ToArray()),
            new("p95", buckets.Select(b => (b.Second, (double?)MetricsCollector.Percentile(b.Values, .95))).ToArray()),
            new("p99", buckets.Select(b => (b.Second, (double?)MetricsCollector.Percentile(b.Values, .99))).ToArray()),
            new("Maximum", buckets.Select(b => (b.Second, (double?)b.Values[^1])).ToArray()));
    }

    private static void Chart(StringBuilder html, string title, string yLabel, double durationSeconds,
        (string Phase, double Start)[] phases, params Series[] series)
        => DrawChart(html, title, yLabel, durationSeconds, phases, null, series);

    private static void DrawChart(StringBuilder html, string title, string yLabel, double durationSeconds,
        (string Phase, double Start)[] phases, double? limitMs, params Series[] series)
    {
        const double width = 1100, height = 310, left = 74, top = 28, chartWidth = 1004, chartHeight = 235;
        var xmax = Math.Max(1, durationSeconds);
        var ymax = Math.Max(1, series.SelectMany(s => s.Points).Where(p => p.Y.HasValue).Select(p => p.Y!.Value).DefaultIfEmpty(1).Max() * 1.08);
        if (limitMs is { } limit) ymax = Math.Max(ymax, limit * 1.08);
        double X(double value) => left + value / xmax * chartWidth;
        double Y(double value) => top + chartHeight - value / ymax * chartHeight;
        html.Append("<section><h2>").Append(E(title)).Append("</h2><svg role=\"img\" aria-label=\"").Append(E(title)).Append("\" viewBox=\"0 0 ").Append(F(width)).Append(' ').Append(F(height)).Append("\"><rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
        for (var i = 0; i <= 4; i++)
        {
            var y = top + chartHeight * i / 4;
            html.Append("<line x1=\"").Append(F(left)).Append("\" y1=\"").Append(F(y)).Append("\" x2=\"").Append(F(left + chartWidth)).Append("\" y2=\"").Append(F(y)).Append("\" stroke=\"#e1e6ed\"/><text x=\"").Append(F(left - 9)).Append("\" y=\"").Append(F(y + 4)).Append("\" text-anchor=\"end\" font-size=\"11\" fill=\"#586575\">").Append(F(ymax * (1 - i / 4d))).Append("</text>");
        }
        for (var i = 0; i <= 5; i++)
        {
            var x = left + chartWidth * i / 5;
            html.Append("<text x=\"").Append(F(x)).Append("\" y=\"282\" text-anchor=\"middle\" font-size=\"11\" fill=\"#586575\">").Append(F(xmax * i / 5)).Append("</text>");
        }
        html.Append("<text x=\"10\" y=\"16\" font-size=\"11\" fill=\"#586575\">").Append(E(yLabel)).Append("</text><text x=\"575\" y=\"303\" text-anchor=\"middle\" font-size=\"11\" fill=\"#586575\">seconds after measurement start</text>");
        if (limitMs is { } threshold)
            html.Append("<line x1=\"").Append(F(left)).Append("\" x2=\"").Append(F(left + chartWidth))
                .Append("\" y1=\"").Append(F(Y(threshold))).Append("\" y2=\"").Append(F(Y(threshold)))
                .Append("\" stroke=\"#b45309\" stroke-dasharray=\"6,4\"/><text x=\"").Append(F(left + chartWidth - 4))
                .Append("\" y=\"").Append(F(Y(threshold) - 5))
                .Append("\" text-anchor=\"end\" font-size=\"11\" fill=\"#b45309\">").Append(F(threshold / 1000)).Append(" s (").Append(F(threshold)).Append(" ms)</text>");
        foreach (var phase in phases)
            html.Append("<line x1=\"").Append(F(X(phase.Start))).Append("\" x2=\"").Append(F(X(phase.Start))).Append("\" y1=\"24\" y2=\"263\" stroke=\"#8793a3\" stroke-dasharray=\"3,5\"/><text x=\"").Append(F(X(phase.Start) + 4)).Append("\" y=\"20\" font-size=\"11\" fill=\"#586575\">").Append(E(phase.Phase)).Append("</text>");
        for (var index = 0; index < series.Length; index++)
        {
            var color = Colors[index % Colors.Length];
            var path = new StringBuilder();
            double? previousX = null;
            foreach (var point in series[index].Points)
            {
                if (point.Y is not { } value || !double.IsFinite(value)) { previousX = null; continue; }
                // A gap represents no observations, rather than an invented interpolated latency.
                var move = previousX is null || point.X - previousX.Value > 2.5;
                path.Append(move ? 'M' : 'L').Append(F(X(point.X))).Append(',').Append(F(Y(value))).Append(' ');
                html.Append("<circle cx=\"").Append(F(X(point.X))).Append("\" cy=\"").Append(F(Y(value))).Append("\" r=\"1.5\" fill=\"").Append(color).Append("\"><title>").Append(E(series[index].Name)).Append(": ").Append(F(value)).Append(" at ").Append(F(point.X)).Append(" s</title></circle>");
                previousX = point.X;
            }
            html.Append("<path d=\"").Append(path).Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"1.8\"/>");
        }
        html.Append("</svg><div class=\"legend\">");
        for (var index = 0; index < series.Length; index++)
            html.Append("<span><i class=\"key\" style=\"background:").Append(Colors[index % Colors.Length]).Append("\"></i>").Append(E(series[index].Name)).Append("</span>");
        if (!series.SelectMany(s => s.Points).Any(p => p.Y.HasValue)) html.Append("<span>No observations recorded.</span>");
        html.Append("</div></section>");
    }

    private static string E(string text) => WebUtility.HtmlEncode(text);
    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
