using System.Diagnostics;
using System.Text.Json;
using Backend.Performance.Tests.Reporting;

namespace Backend.Performance.Tests.Infrastructure;

/// <summary>Regression check: a slow OS resource query must not pause measured clients.</summary>
internal static class MetricsSamplingSelfCheck
{
    public static async Task RunAsync(string output)
    {
        var directory = Path.Combine(output, "blocked-resource-sampling");
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRead = new ManualResetEventSlim();
        var reads = 0;
        await using var metrics = new MetricsCollector(directory, null, _ =>
        {
            var read = Interlocked.Increment(ref reads);
            if (read == 2)
            {
                readStarted.TrySetResult();
                releaseRead.Wait();
            }
            return new ProcessSnapshot("synthetic-process", TimeSpan.FromSeconds(read * 10), 1024, 2048);
        });

        // Establish an earlier generation and CPU baseline before resetting measurement.
        metrics.SetPhase("warmup", 1);
        metrics.BeginMeasurement();
        var warmup = metrics.Capture();
        metrics.RecordOperation("http", "warmup-before-reset", warmup, true, 200);

        var measurement = Task.Factory.StartNew(metrics.BeginMeasurement, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var callbacks = Task.Run(() =>
            {
                metrics.SetPhase("steady-state", 1);
                var started = metrics.Capture();
                metrics.RecordOperation("http", "during-resource-read", started, true, 200,
                    completedTimestamp: started.Timestamp + Stopwatch.Frequency / 100);
                metrics.RecordOperation("http", "late-warmup", warmup, true, 200);
                metrics.NotificationReceived("synthetic-student", "synthetic-request", "synthetic-event");
                metrics.ConnectionOpened("synthetic-student");
                metrics.ConnectionReconnecting("synthetic-student");
                metrics.ConnectionReconnected("synthetic-student");
                Require(metrics.ActualConnections == 1,
                    "Connection callbacks and the actual gauge must work during a blocked resource read.");
            });
            await callbacks.WaitAsync(TimeSpan.FromSeconds(5));
            Require(!releaseRead.IsSet,
                "HTTP timestamps, recording, notifications and connections must progress before the OS read is released.");
        }
        finally
        {
            releaseRead.Set();
            await measurement.WaitAsync(TimeSpan.FromSeconds(5));
        }

        await metrics.ExportAsync(new
        {
            Outcome = "completed",
            Purpose = "Synthetic blocked-resource-sampling regression; no gateway performance was measured"
        });
        using var raw = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "raw.json")));
        var root = raw.RootElement;
        var operations = root.GetProperty("Operations").EnumerateArray().ToArray();
        Require(operations.Length == 1 && operations[0].GetProperty("Operation").GetString() == "during-resource-read",
            "Reset must exclude both earlier observations and late completions from the warm-up generation.");
        Require(root.GetProperty("Notifications").GetArrayLength() == 1 &&
            root.GetProperty("Connections").GetArrayLength() == 3,
            "Callbacks completed during the stalled process read must remain in the export.");
        var samples = root.GetProperty("Samples").EnumerateArray().ToArray();
        Require(samples.Length == 2 &&
            samples[0].GetProperty("Processes")[0].GetProperty("CpuPercent").ValueKind == JsonValueKind.Null &&
            samples[1].GetProperty("Processes")[0].GetProperty("CpuPercent").ValueKind == JsonValueKind.Number,
            "CPU sampling must discard the earlier generation's baseline and resume with a fresh baseline.");
        Require(samples[^1].GetProperty("ActualConnections").GetInt32() == 1 &&
            samples[^1].GetProperty("HttpCompleted").GetInt32() == 1 &&
            samples[^1].GetProperty("SignalRMessages").GetInt32() == 1,
            "The following load sample must include the HTTP, notification and connection activity.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Resource sampling self-check failed: {message}");
    }
}
