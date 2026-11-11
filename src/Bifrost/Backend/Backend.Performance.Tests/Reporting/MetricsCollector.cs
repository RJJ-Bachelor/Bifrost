using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Backend.Performance.Tests.Reporting;

/// <summary>One generator's monotonic clock; also identifies warm-up samples that must be discarded.</summary>
public readonly record struct OperationStamp(long Timestamp, string Phase, int Generation);

public sealed record OperationMeasurement(
    double ElapsedSeconds, string Phase, string Category, string Operation, double DurationMs,
    bool Success, int? StatusCode, string? Actor, string? CorrelationId, string? Error,
    long StartedTimestamp, long CompletedTimestamp);

public sealed record ConnectionMeasurement(
    double ElapsedSeconds, string Phase, string Event, string Actor, int ActualConnections, string? Reason);

public sealed record NotificationMeasurement(
    double ElapsedSeconds, string Phase, string Actor, string? CorrelationId, string? EventName, long ReceivedTimestamp);

public sealed record ProcessMeasurement(
    int ProcessId, string ProcessName, double? CpuPercent, long? WorkingSetBytes,
    long? PrivateMemoryBytes, string? Error);

internal sealed record ProcessSnapshot(
    string ProcessName, TimeSpan Cpu, long WorkingSetBytes, long PrivateMemoryBytes);

public sealed record LoadSample(
    double ElapsedSeconds, string Phase, int TargetConnections, int ActualConnections,
    long ConnectionsOpened, long ConnectionsClosed, long Reconnects, long HttpCompleted,
    long HttpFailures, double HttpRequestsPerSecond, double HttpFailuresPerSecond,
    long SignalRMessages, double SignalRMessagesPerSecond,
    IReadOnlyList<ProcessMeasurement> Processes);

public sealed record OperationSummary(
    string Phase, string Category, string Operation, long Count, long Failures,
    double P50Ms, double P95Ms, double P99Ms, double MaximumMs, long AtOrOverOneSecond,
    double FailurePercent);

public sealed record RunStatus(string Outcome, string? Failure, bool MeasurementStarted);

internal sealed record ReportDiagnostics(
    long HttpRequests, long HttpFailures, long HttpAtOrOverOneSecond,
    long ConnectionAttempts, long ConnectionFailures, long SuccessfulConnectionsAtOrOverOneSecond,
    long NotificationMeasurements, long NotificationFailures, long IterationFailures,
    long OtherFailedObservations, long PlannedConnectionClosures,
    long UnclassifiedShutdownClosures, long UnexpectedConnectionClosures,
    long ReconnectingEvents, long ReconnectedEvents);

/// <summary>
/// Captures observations independently of NBomber's scenario timings. HTTP and notification timings
/// use Stopwatch timestamps in this process; no client/server wall-clock synchronization is assumed.
/// </summary>
public sealed class MetricsCollector : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly object _sampleGate = new();
    private readonly string _outputDirectory;
    private readonly int[] _processIds;
    private readonly Func<int, ProcessSnapshot> _readProcess;
    private readonly HashSet<string> _connected = new(StringComparer.Ordinal);
    private readonly HashSet<string> _plannedClosures = new(StringComparer.Ordinal);
    private readonly List<OperationMeasurement> _operations = [];
    private readonly List<ConnectionMeasurement> _connections = [];
    private readonly List<NotificationMeasurement> _notifications = [];
    private readonly List<LoadSample> _samples = [];
    private readonly Dictionary<int, (long Timestamp, TimeSpan Cpu)> _processBaselines = [];
    private readonly CancellationTokenSource _samplingCancellation = new();
    private Task? _samplingTask;
    private string _phase = "setup";
    private int _targetConnections;
    private int _generation;
    private bool _measuring;
    private long _measurementStarted;
    private DateTimeOffset _measurementStartedUtc;
    private long _connectionsOpened;
    private long _connectionsClosed;
    private long _reconnects;
    private long _httpCompleted;
    private long _httpFailures;
    private long _previousHttpCompleted;
    private long _previousHttpFailures;
    private long _signalRMessages;
    private long _previousSignalRMessages;
    private long _lastSampleTimestamp;

    public MetricsCollector(string outputDirectory, IReadOnlyList<int>? processIds = null)
        : this(outputDirectory, processIds, ReadProcess) { }

    internal MetricsCollector(string outputDirectory, IReadOnlyList<int>? processIds,
        Func<int, ProcessSnapshot> readProcess)
    {
        _outputDirectory = Path.GetFullPath(outputDirectory);
        _processIds = new[] { Environment.ProcessId }.Concat(processIds ?? []).Distinct().ToArray();
        _readProcess = readProcess;
    }

    public int ActualConnections { get { lock (_gate) return _connected.Count; } }

    public void StartSampling()
    {
        lock (_gate)
            _samplingTask ??= SampleLoopAsync(_samplingCancellation.Token);
    }

    /// <summary>Call only after warm-up actors have been disposed. Gauges retain their actual value.</summary>
    public void BeginMeasurement()
    {
        // Sampling and reset use the same lock order. A resource read from an earlier
        // generation must finish before its CPU baseline can be discarded.
        lock (_sampleGate)
        lock (_gate)
        {
            _generation++;
            _measuring = true;
            _measurementStarted = Stopwatch.GetTimestamp();
            _measurementStartedUtc = DateTimeOffset.UtcNow;
            _operations.Clear();
            _connections.Clear();
            _plannedClosures.Clear();
            _notifications.Clear();
            _samples.Clear();
            _processBaselines.Clear();
            _connectionsOpened = _connectionsClosed = _reconnects = 0;
            _httpCompleted = _httpFailures = _previousHttpCompleted = _previousHttpFailures = 0;
            _signalRMessages = _previousSignalRMessages = 0;
            _lastSampleTimestamp = _measurementStarted;
        }
        Sample();
    }

    public void SetPhase(string phase, int targetConnections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ArgumentOutOfRangeException.ThrowIfNegative(targetConnections);
        lock (_gate)
        {
            _phase = phase;
            _targetConnections = targetConnections;
        }
    }

    public OperationStamp Capture()
    {
        lock (_gate)
            return new(Stopwatch.GetTimestamp(), _phase, _measuring ? _generation : -1);
    }

    public void RecordOperation(
        string category, string operation, OperationStamp started, bool success,
        int? statusCode = null, string? actor = null, string? correlationId = null,
        string? error = null, long? completedTimestamp = null)
    {
        var ended = completedTimestamp ?? Stopwatch.GetTimestamp();
        lock (_gate)
        {
            if (!_measuring || started.Generation != _generation || started.Timestamp < _measurementStarted)
                return;
            if (ended < started.Timestamp)
                throw new ArgumentOutOfRangeException(nameof(completedTimestamp), "Completion precedes start on the monotonic clock.");

            _operations.Add(new(
                Elapsed(started.Timestamp), started.Phase, category, operation,
                Stopwatch.GetElapsedTime(started.Timestamp, ended).TotalMilliseconds,
                success, statusCode, actor, correlationId, error, started.Timestamp, ended));
            if (string.Equals(category, "http", StringComparison.OrdinalIgnoreCase))
            {
                _httpCompleted++;
                if (!success) _httpFailures++;
            }
        }
    }

    public void RecordFailure(string operation, string error, string? actor = null) =>
        RecordOperation("failure", operation, Capture(), false, actor: actor, error: error);

    /// <summary>Counts an actual message without inventing a latency sample for unmatched broadcasts.</summary>
    public void NotificationReceived(string actor, string? correlationId = null, string? eventName = null)
    {
        var timestamp = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            if (!_measuring) return;
            _signalRMessages++;
            _notifications.Add(new(Elapsed(timestamp), _phase, actor, correlationId, eventName, timestamp));
        }
    }

    public void ConnectionOpened(string actor)
    {
        lock (_gate)
        {
            _plannedClosures.Remove(actor);
            if (!_connected.Add(actor)) return;
            if (!_measuring) return;
            _connectionsOpened++;
            AddConnectionEvent("opened", actor, null);
        }
    }

    /// <summary>Mark before stopping the hub: its Closed callback can run before StopAsync returns.</summary>
    public void ConnectionCloseScheduled(string actor)
    {
        lock (_gate) _plannedClosures.Add(actor);
    }

    public void ConnectionClosed(string actor, string? reason = null)
    {
        lock (_gate)
        {
            var planned = _plannedClosures.Remove(actor);
            if (!_connected.Remove(actor)) return;
            if (!_measuring) return;
            _connectionsClosed++;
            // Planned intent only explains an otherwise clean callback. Preserve transport
            // exceptions even when they coincide with shutdown instead of hiding them.
            if (planned && string.IsNullOrWhiteSpace(reason)) reason = "Scheduled disconnect.";
            AddConnectionEvent("closed", actor, reason);
        }
    }

    public void ConnectionReconnecting(string actor, string? reason = null)
    {
        lock (_gate)
        {
            if (_connected.Remove(actor) && _measuring) _connectionsClosed++;
            if (_measuring) AddConnectionEvent("reconnecting", actor, reason);
        }
    }

    public void ConnectionReconnected(string actor)
    {
        lock (_gate)
        {
            var added = _connected.Add(actor);
            if (!_measuring) return;
            if (added) _connectionsOpened++;
            _reconnects++;
            AddConnectionEvent("reconnected", actor, null);
        }
    }

    private void AddConnectionEvent(string eventName, string actor, string? reason) =>
        _connections.Add(new(Elapsed(Stopwatch.GetTimestamp()), _phase, eventName, actor, _connected.Count, reason));

    private double Elapsed(long timestamp) => Stopwatch.GetElapsedTime(_measurementStarted, timestamp).TotalSeconds;

    private async Task SampleLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken)) Sample();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void Sample()
    {
        // Windows process queries can stall. They must never hold the lock used by
        // HTTP timings and SignalR callbacks, or the measurement changes the load.
        lock (_sampleGate)
        {
            LoadSample sample;
            lock (_gate)
            {
                if (!_measuring) return;
                var now = Stopwatch.GetTimestamp();
                var interval = Stopwatch.GetElapsedTime(_lastSampleTimestamp, now).TotalSeconds;
                sample = new(
                    Elapsed(now), _phase, _targetConnections, _connected.Count,
                    _connectionsOpened, _connectionsClosed, _reconnects, _httpCompleted, _httpFailures,
                    interval > 0 ? (_httpCompleted - _previousHttpCompleted) / interval : 0,
                    interval > 0 ? (_httpFailures - _previousHttpFailures) / interval : 0,
                    _signalRMessages, interval > 0 ? (_signalRMessages - _previousSignalRMessages) / interval : 0,
                    []);
                _lastSampleTimestamp = now;
                _previousHttpCompleted = _httpCompleted;
                _previousHttpFailures = _httpFailures;
                _previousSignalRMessages = _signalRMessages;
            }

            var processes = _processIds.Select(SampleProcess).ToArray();
            lock (_gate) _samples.Add(sample with { Processes = processes });
        }
    }

    private ProcessMeasurement SampleProcess(int processId)
    {
        try
        {
            var process = _readProcess(processId);
            var now = Stopwatch.GetTimestamp();
            var cpu = process.Cpu;
            double? percent = null;
            if (_processBaselines.TryGetValue(processId, out var baseline))
            {
                var elapsed = Stopwatch.GetElapsedTime(baseline.Timestamp, now).TotalSeconds;
                if (elapsed > 0)
                    percent = Math.Max(0, (cpu - baseline.Cpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100);
            }
            _processBaselines[processId] = (now, cpu);
            return new(processId, process.ProcessName, percent, process.WorkingSetBytes, process.PrivateMemoryBytes, null);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            _processBaselines.Remove(processId);
            return new(processId, "unavailable", null, null, null, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static ProcessSnapshot ReadProcess(int processId)
    {
        using var process = Process.GetProcessById(processId);
        process.Refresh();
        return new(process.ProcessName, process.TotalProcessorTime, process.WorkingSet64, process.PrivateMemorySize64);
    }

    /// <summary>Stops sampling and writes observed data. Missing server metrics remain missing.</summary>
    public async Task ExportAsync(object configuration, CancellationToken cancellationToken = default)
    {
        await StopSamplingAsync();
        Sample();
        OperationMeasurement[] operations;
        ConnectionMeasurement[] connections;
        NotificationMeasurement[] notifications;
        LoadSample[] samples;
        DateTimeOffset startedUtc;
        double durationSeconds;
        int finalConnections;
        bool measurementStarted;
        lock (_gate)
        {
            operations = _operations.ToArray();
            connections = _connections.ToArray();
            notifications = _notifications.ToArray();
            samples = _samples.ToArray();
            startedUtc = _measurementStartedUtc;
            durationSeconds = _measuring ? Elapsed(Stopwatch.GetTimestamp()) : 0;
            finalConnections = _connected.Count;
            measurementStarted = _measuring;
        }
        Directory.CreateDirectory(_outputDirectory);
        var summaries = Summarize(operations);
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var safeConfiguration = Redact(JsonSerializer.SerializeToNode(configuration, jsonOptions));
        var status = new RunStatus(safeConfiguration?["Outcome"]?.GetValue<string>() ?? "unknown",
            safeConfiguration?["Failure"]?.GetValue<string>(), measurementStarted);
        var expectedQueueMeasurements = safeConfiguration?["Options"]?["Lessons"]?.GetValue<int>() ?? 1;
        var requirements = PerformanceRequirements.Evaluate(operations, expectedQueueMeasurements);
        var raw = new { RunStatus = status, RequirementsVersion = PerformanceRequirements.Version, ExpectedQueueStartMeasurements = expectedQueueMeasurements, StopwatchFrequency = Stopwatch.Frequency, MeasurementStartedUtc = startedUtc, DurationSeconds = durationSeconds, Operations = operations, Connections = connections, Notifications = notifications, Samples = samples };
        await WriteJsonAsync("raw.json", raw, jsonOptions, cancellationToken);
        await WriteJsonAsync("summary.json", new
        {
            RunStatus = status,
            RequirementsVersion = PerformanceRequirements.Version,
            Requirements = requirements,
            MeasurementStartedUtc = startedUtc,
            DurationSeconds = durationSeconds,
            FinalActualConnections = finalConnections,
            PeakActualConnections = samples.Select(s => s.ActualConnections).Concat(connections.Select(c => c.ActualConnections)).DefaultIfEmpty().Max(),
            HttpRequests = operations.LongCount(o => o.Category.Equals("http", StringComparison.OrdinalIgnoreCase)),
            HttpFailures = operations.LongCount(o => o.Category.Equals("http", StringComparison.OrdinalIgnoreCase) && !o.Success),
            TotalFailures = operations.LongCount(o => !o.Success && !o.Category.Equals("cancelled", StringComparison.OrdinalIgnoreCase)),
            FailureCountMethod = "TotalFailures counts failed observations, not independent incidents. A timed-out notification and its failed scenario iteration may describe the same underlying incident; see Diagnostics for separate counts.",
            Diagnostics = Diagnose(operations, connections),
            CancelledOperations = operations.LongCount(o => o.Category.Equals("cancelled", StringComparison.OrdinalIgnoreCase)),
            SignalRMessages = notifications.LongLength,
            ProcessIds = _processIds,
            PercentileMethod = "Nearest rank (ceil(p * n)); completed observations include failures. Generic >=1000ms counts are descriptive only; requirement violations use >1000ms for registration and >10000ms for the two notification flows.",
            CancellationMethod = "Cancelled operations retain Success=false in raw data and their own summary category; excluded from TotalFailures and HTTP throughput/failures.",
            NotificationClock = "Stopwatch timestamps on the load generator; HTTP send to matching SignalR callback.",
            Configuration = safeConfiguration,
            Operations = summaries
        }, jsonOptions, cancellationToken);

        await File.WriteAllTextAsync(Path.Combine(_outputDirectory, "operations.csv"), OperationsCsv(operations), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_outputDirectory, "connections.csv"), ConnectionsCsv(connections), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_outputDirectory, "notifications.csv"), NotificationsCsv(notifications), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_outputDirectory, "load.csv"), LoadCsv(samples), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_outputDirectory, "resources.csv"), ResourcesCsv(samples), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_outputDirectory, "summary.csv"), SummaryCsv(summaries), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_outputDirectory, "report.html"), HtmlReport.Render(startedUtc, durationSeconds, finalConnections, samples, operations, summaries, status, connections, requirements), cancellationToken);
    }

    public static async Task RenderSavedReportAsync(string directory)
    {
        using var raw = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "raw.json")));
        var root = raw.RootElement;
        var operations = root.GetProperty("Operations").Deserialize<OperationMeasurement[]>()!;
        var samples = root.GetProperty("Samples").Deserialize<LoadSample[]>()!;
        var connections = root.GetProperty("Connections").Deserialize<ConnectionMeasurement[]>()!;
        var startedUtc = root.GetProperty("MeasurementStartedUtc").GetDateTimeOffset();
        RunStatus status;
        if (root.TryGetProperty("RunStatus", out var savedStatus))
            status = savedStatus.Deserialize<RunStatus>()!;
        else
        {
            // Older raw exports omitted run outcome. Recover the original startup failure from
            // their summary rather than displaying an empty, seemingly successful load report.
            var outcome = "unknown";
            string? failure = null;
            var summaryPath = Path.Combine(directory, "summary.json");
            if (File.Exists(summaryPath))
            {
                using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(summaryPath));
                if (summary.RootElement.TryGetProperty("Configuration", out var configuration))
                {
                    if (configuration.TryGetProperty("Outcome", out var savedOutcome)) outcome = savedOutcome.GetString() ?? "unknown";
                    if (configuration.TryGetProperty("Failure", out var savedFailure)) failure = savedFailure.GetString();
                }
            }
            status = new RunStatus(outcome, failure, startedUtc != default);
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "report.html"), HtmlReport.Render(
            startedUtc,
            root.GetProperty("DurationSeconds").GetDouble(),
            samples.LastOrDefault()?.ActualConnections ?? connections.LastOrDefault()?.ActualConnections ?? 0,
            samples, operations, Summarize(operations), status, connections,
            root.TryGetProperty("RequirementsVersion", out var version) && version.GetString() == PerformanceRequirements.Version
                ? PerformanceRequirements.Evaluate(operations, root.TryGetProperty("ExpectedQueueStartMeasurements", out var expected) ? expected.GetInt32() : 1) : null));
    }

    private Task WriteJsonAsync<T>(string filename, T value, JsonSerializerOptions options, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(Path.Combine(_outputDirectory, filename), JsonSerializer.Serialize(value, options), cancellationToken);

    private static JsonNode? Redact(JsonNode? node)
    {
        if (node is JsonObject dictionary)
        {
            foreach (var key in dictionary.Select(item => item.Key).ToArray())
            {
                if (new[] { "password", "secret", "token", "cookie", "credentials", "authorization" }.Any(word => key.Contains(word, StringComparison.OrdinalIgnoreCase)))
                    dictionary[key] = "[redacted]";
                else Redact(dictionary[key]);
            }
        }
        else if (node is JsonArray array)
            foreach (var child in array) Redact(child);
        return node;
    }

    internal static OperationSummary[] Summarize(IReadOnlyList<OperationMeasurement> operations)
    {
        var groups = operations.GroupBy(o => (o.Phase, o.Category, o.Operation))
            .Concat(operations.GroupBy(o => (Phase: "all", o.Category, o.Operation)));
        return groups.Select(group =>
        {
            var values = group.Select(o => o.DurationMs).Order().ToArray();
            var failures = group.LongCount(o => !o.Success);
            return new OperationSummary(group.Key.Phase, group.Key.Category, group.Key.Operation, values.LongLength, failures,
                Percentile(values, .50), Percentile(values, .95), Percentile(values, .99), values[^1],
                values.LongCount(ms => ms >= 1000), failures * 100d / values.Length);
        }).OrderBy(s => s.Phase).ThenBy(s => s.Category).ThenBy(s => s.Operation).ToArray();
    }

    internal static bool IsPlannedClosureReason(string? reason) =>
        reason is "Scheduled disconnect." or "Scheduled browser closure.";

    internal static bool IsUnclassifiedShutdownClosure(ConnectionMeasurement connection) =>
        connection.Event == "closed" && string.IsNullOrWhiteSpace(connection.Reason) &&
        connection.Phase is "ramp-down" or "cleanup";

    internal static ReportDiagnostics Diagnose(IReadOnlyList<OperationMeasurement> operations,
        IReadOnlyList<ConnectionMeasurement> connections)
    {
        static bool Category(OperationMeasurement operation, string category) =>
            operation.Category.Equals(category, StringComparison.OrdinalIgnoreCase);
        static bool Iteration(OperationMeasurement operation) => Category(operation, "failure") &&
            operation.Operation is "student_iteration" or "teacher_iteration";
        return new(
            operations.LongCount(o => Category(o, "http")),
            operations.LongCount(o => Category(o, "http") && !o.Success),
            operations.LongCount(o => Category(o, "http") && o.DurationMs >= 1000),
            operations.LongCount(o => Category(o, "connection")),
            operations.LongCount(o => Category(o, "connection") && !o.Success),
            operations.LongCount(o => Category(o, "connection") && o.Success && o.DurationMs >= 1000),
            operations.LongCount(o => Category(o, "notification")),
            operations.LongCount(o => Category(o, "notification") && !o.Success),
            operations.LongCount(o => Iteration(o) && !o.Success),
            operations.LongCount(o => !o.Success && !Category(o, "http") && !Category(o, "connection") &&
                !Category(o, "notification") && !Category(o, "cancelled") && !Iteration(o)),
            connections.LongCount(c => c.Event == "closed" && IsPlannedClosureReason(c.Reason)),
            connections.LongCount(IsUnclassifiedShutdownClosure),
            connections.LongCount(c => c.Event == "closed" && !IsPlannedClosureReason(c.Reason) &&
                !IsUnclassifiedShutdownClosure(c)),
            connections.LongCount(c => c.Event == "reconnecting"),
            connections.LongCount(c => c.Event == "reconnected"));
    }

    internal static double Percentile(double[] ordered, double percentile) =>
        ordered.Length == 0 ? 0 : ordered[Math.Max(0, (int)Math.Ceiling(percentile * ordered.Length) - 1)];

    private static string Cell(object? value)
    {
        var text = value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "";
        // Quote all values. CSV consumers must also avoid treating arbitrary text as spreadsheet formulas.
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    private static void Row(StringBuilder builder, params object?[] cells) => builder.AppendLine(string.Join(",", cells.Select(Cell)));

    private static string OperationsCsv(IEnumerable<OperationMeasurement> values)
    {
        var csv = new StringBuilder("elapsed_seconds,phase,category,operation,duration_ms,success,status_code,actor,correlation_id,error,started_stopwatch_timestamp,completed_stopwatch_timestamp\n");
        foreach (var v in values) Row(csv, v.ElapsedSeconds, v.Phase, v.Category, v.Operation, v.DurationMs, v.Success, v.StatusCode, v.Actor, v.CorrelationId, v.Error, v.StartedTimestamp, v.CompletedTimestamp);
        return csv.ToString();
    }

    private static string ConnectionsCsv(IEnumerable<ConnectionMeasurement> values)
    {
        var csv = new StringBuilder("elapsed_seconds,phase,event,actor,actual_connections,reason\n");
        foreach (var v in values) Row(csv, v.ElapsedSeconds, v.Phase, v.Event, v.Actor, v.ActualConnections, v.Reason);
        return csv.ToString();
    }

    private static string LoadCsv(IEnumerable<LoadSample> values)
    {
        var csv = new StringBuilder("elapsed_seconds,phase,target_connections,actual_connections,connections_opened,connections_closed,reconnects,http_completed,http_failures,http_requests_per_second,http_failures_per_second,signalr_messages,signalr_messages_per_second\n");
        foreach (var v in values) Row(csv, v.ElapsedSeconds, v.Phase, v.TargetConnections, v.ActualConnections, v.ConnectionsOpened, v.ConnectionsClosed, v.Reconnects, v.HttpCompleted, v.HttpFailures, v.HttpRequestsPerSecond, v.HttpFailuresPerSecond, v.SignalRMessages, v.SignalRMessagesPerSecond);
        return csv.ToString();
    }

    private static string NotificationsCsv(IEnumerable<NotificationMeasurement> values)
    {
        var csv = new StringBuilder("elapsed_seconds,phase,actor,correlation_id,event_name,received_stopwatch_timestamp\n");
        foreach (var v in values) Row(csv, v.ElapsedSeconds, v.Phase, v.Actor, v.CorrelationId, v.EventName, v.ReceivedTimestamp);
        return csv.ToString();
    }

    private static string ResourcesCsv(IEnumerable<LoadSample> values)
    {
        var csv = new StringBuilder("elapsed_seconds,phase,process_id,process_name,cpu_percent_of_machine,working_set_bytes,private_memory_bytes,error\n");
        foreach (var v in values)
            foreach (var p in v.Processes) Row(csv, v.ElapsedSeconds, v.Phase, p.ProcessId, p.ProcessName, p.CpuPercent, p.WorkingSetBytes, p.PrivateMemoryBytes, p.Error);
        return csv.ToString();
    }

    private static string SummaryCsv(IEnumerable<OperationSummary> values)
    {
        var csv = new StringBuilder("phase,category,operation,count,failures,p50_ms,p95_ms,p99_ms,maximum_ms,at_or_over_1000_ms,failure_percent\n");
        foreach (var v in values) Row(csv, v.Phase, v.Category, v.Operation, v.Count, v.Failures, v.P50Ms, v.P95Ms, v.P99Ms, v.MaximumMs, v.AtOrOverOneSecond, v.FailurePercent);
        return csv.ToString();
    }

    private async Task StopSamplingAsync()
    {
        await _samplingCancellation.CancelAsync();
        Task? samplingTask;
        lock (_gate) samplingTask = _samplingTask;
        if (samplingTask is not null) await samplingTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopSamplingAsync();
        _samplingCancellation.Dispose();
    }
}
