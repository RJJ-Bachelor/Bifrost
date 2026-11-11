using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Performance.Tests.Configuration;
using Backend.Performance.Tests.Eir;
using Backend.Performance.Tests.Reporting;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace Backend.Performance.Tests.Infrastructure;

/// <summary>Deterministic local checks of the harness; these are never gateway performance results.</summary>
public static class SelfCheck
{
    public static async Task RunAsync()
    {
        var output = Path.Combine(FindWorkspace(), "src", "Bifrost", "Backend", "Backend.Performance.Tests",
            ".local", $"performance-self-check-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "SYNTHETIC-SELF-CHECK.txt"),
            "Synthetic no-network harness checks only. No gateway, HTTP load or real SignalR connections were exercised. These files are not performance-test results.\n");

        var options = new PerformanceOptions { Lessons = 3, LessonWeights = [5, 3, 2] };
        options.Validate();
        var assignment = Enumerable.Range(0, 100).Select(options.LessonFor).ToArray();
        Require(assignment.Count(value => value == 0) == 50 && assignment.Count(value => value == 1) == 30 &&
            assignment.Count(value => value == 2) == 20, "Weighted lesson distribution must preserve the configured 5:3:2 split.");
        var rejected = false;
        try { (options with { LessonWeights = [1, 0, 1] }).Validate(); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "Zero lesson weights must be rejected.");

        await using var metrics = new MetricsCollector(output);
        metrics.SetPhase("warmup", 1);
        var warmup = metrics.Capture();
        metrics.RecordOperation("http", "warmup-before", warmup, true, 200);
        metrics.ConnectionOpened("synthetic-warmup");
        Require(metrics.ActualConnections == 1, "Connection gauge must work before measurement.");
        metrics.ConnectionClosed("synthetic-warmup");
        metrics.BeginMeasurement();
        metrics.SetPhase("steady-state", 1);
        metrics.RecordOperation("http", "late-warmup", warmup, true, 200);

        metrics.ConnectionOpened("synthetic-student");
        metrics.ConnectionOpened("synthetic-student");
        Require(metrics.ActualConnections == 1, "Repeated connection events must not inflate the gauge.");
        metrics.ConnectionReconnecting("synthetic-student");
        Require(metrics.ActualConnections == 0, "Reconnecting must remove the lost connection from the actual gauge.");
        metrics.ConnectionReconnected("synthetic-student");
        Require(metrics.ActualConnections == 1, "Reconnected must restore the actual gauge.");
        metrics.ConnectionClosed("synthetic-student");
        metrics.ConnectionClosed("synthetic-student");
        Require(metrics.ActualConnections == 0, "Repeated close events must leave the gauge at zero.");

        var tracker = new NotificationTracker(metrics);
        var sessionId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        NotificationDeliveryRequestedMessage Message(string recipient, Guid entity) => new(
            Guid.NewGuid(), recipient, "synthetic", DateTimeOffset.UtcNow,
            sessionId, entity, 1, "Created", EventType: NotificationEventType.HelpRequestCreated);

        // Simulate the important race: SignalR arrives before POST's response reveals the new ID.
        using (var early = tracker.Expect("early-matching", "student-a", sessionId, "Created",
                   eventType: NotificationEventType.HelpRequestCreated))
        {
            early.MarkHttpSent();
            tracker.Observe(Message("student-a", Guid.NewGuid()), "student-a");
            tracker.Observe(Message("student-a", requestId), "student-a");
            early.BindRequest(requestId);
            Require(await early.WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None),
                "A matching notification received before response binding must complete the expectation.");
        }

        // Other users/entities are common during load and must never satisfy this operation.
        using (var missing = tracker.Expect("wrong-recipient-and-entity", "student-a", sessionId,
                   "Created", requestId, NotificationEventType.HelpRequestCreated))
        {
            missing.MarkHttpSent();
            tracker.Observe(Message("student-b", requestId), "student-b");
            tracker.Observe(Message("student-a", requestId), "student-b");
            tracker.Observe(Message("student-a", Guid.NewGuid()), "student-a");
            tracker.Observe(Message("student-a", requestId) with { SessionId = Guid.NewGuid() }, "student-a");
            tracker.Observe(Message("student-a", requestId) with { Change = "Reserved" }, "student-a");
            tracker.Observe(Message("student-a", requestId) with { EventType = NotificationEventType.HelpRequestAnswered }, "student-a");
            Require(!await missing.WaitAsync(TimeSpan.FromMilliseconds(25), CancellationToken.None),
                "Only the actual authenticated recipient, session, entity, change and event may satisfy an expectation; a payload claiming the owner on another client's connection is insufficient.");
        }

        using (var beforeHttp = tracker.Expect("before-http", "student-a", sessionId, "Created", requestId))
        {
            tracker.Observe(Message("student-a", requestId), "student-a");
            beforeHttp.MarkHttpSent();
            Require(!await beforeHttp.WaitAsync(TimeSpan.FromMilliseconds(25), CancellationToken.None),
                "A notification observed before HTTP send must not satisfy a later request.");
        }

        var threshold = metrics.Capture();
        metrics.RecordOperation("synthetic", "one-second-threshold", threshold, true,
            completedTimestamp: threshold.Timestamp + Stopwatch.Frequency);
        Require(MetricsCollector.Percentile([1, 4, 1000, 2000], .50) == 4 &&
            MetricsCollector.Percentile([1, 4, 1000, 2000], .95) == 2000 &&
            MetricsCollector.Percentile([1, 4, 1000, 2000], .99) == 2000,
            "Percentiles must use the documented nearest-rank calculation.");

        const string sentinel = "SYNTHETIC_SECRET_MUST_BE_REDACTED";
        metrics.RecordOperation("cancelled", "scheduled-close", metrics.Capture(), false,
            error: "Synthetic planned cancellation");
        await metrics.ExportAsync(new
        {
            Purpose = "Synthetic no-network self-check; never performance-test evidence",
            Password = sentinel,
            Nested = new { AccessToken = sentinel, Cookie = sentinel, ClientSecret = sentinel },
            Values = new[] { new { Authorization = sentinel } }
        });
        var summaryText = await File.ReadAllTextAsync(Path.Combine(output, "summary.json"));
        Require(!summaryText.Contains(sentinel, StringComparison.Ordinal), "Exported configuration must redact nested secrets.");
        using var summary = JsonDocument.Parse(summaryText);
        var root = summary.RootElement;
        Require(root.GetProperty("FinalActualConnections").GetInt32() == 0, "Export must report actual final connections.");
        Require(root.GetProperty("HttpRequests").GetInt32() == 0, "Warm-up operations, including delayed completions, must be excluded.");
        Require(root.GetProperty("TotalFailures").GetInt32() == 2, "Missing notifications must be recorded as failures.");
        Require(root.GetProperty("CancelledOperations").GetInt32() == 1,
            "Planned cancellations must stay visible without being mistaken for transport failures.");
        var summaries = root.GetProperty("Operations").EnumerateArray().ToArray();
        var oneSecond = summaries.Single(value => value.GetProperty("Phase").GetString() == "all" &&
            value.GetProperty("Operation").GetString() == "one-second-threshold");
        Require(oneSecond.GetProperty("AtOrOverOneSecond").GetInt32() == 1,
            "The one-second boundary must count as a threshold exceedance.");
        foreach (var filename in new[] { "raw.json", "operations.csv", "load.csv", "resources.csv", "report.html" })
            Require(File.Exists(Path.Combine(output, filename)), $"Missing export artifact {filename}.");
        await MetricsCollector.RenderSavedReportAsync(output);
        var regeneratedHtml = await File.ReadAllTextAsync(Path.Combine(output, "report.html"));
        Require(regeneratedHtml.Contains("1 s (1000 ms)") && regeneratedHtml.Contains("10 s (10000 ms)"),
            "Current reports must show the registration's one-second limit and the notifications' ten-second limit.");
        await VerifyRequirementRulesAsync(output);
        await VerifyNotificationBudgetAsync(output);
        await VerifyFailedAnswerProbeAsync(output);
        await VerifyStartupReportingAsync(output);
        await VerifyFailureDiagnosticsAsync(output);
        await VerifyIdentityCookieIsolationAsync();
        await MetricsSamplingSelfCheck.RunAsync(output);
        Console.WriteLine($"Synthetic no-network self-check passed. No gateway performance was measured. Artifacts: {output}");
    }

    private static async Task VerifyRequirementRulesAsync(string output)
    {
        (string Id, string Category, string Operation, double Limit)[] rules =
        [
            ("K1", "http", PerformanceRequirements.CreateOperation, 1000),
            ("K2", "notification", PerformanceRequirements.QueueOperation, 10000),
            ("K3", "notification", PerformanceRequirements.AnswerOperation, 10000)
        ];
        static OperationMeasurement Observation(string category, string operation, double duration, bool success = true) =>
            new(0, "steady-state", category, operation, duration, success, null, "synthetic", null, null, 0, 0);
        var boundaries = rules.Select(rule => Observation(rule.Category, rule.Operation, rule.Limit)).ToArray();
        OperationMeasurement[] diagnostics =
        [
            Observation("http", "GET /api/students/requests", 30000, false),
            Observation("connection", "signalr.connect", 30000, false),
            Observation("notification", "student.create", 30000, false),
            Observation("browser", "visible-feedback", 30000, false),
            Observation("notification", PerformanceRequirements.CreateOperation, 30000, false),
            Observation("http", PerformanceRequirements.QueueOperation, 30000, false)
        ];
        var observations = boundaries.Concat(diagnostics).ToArray();
        var results = PerformanceRequirements.Evaluate(observations);
        Require(results.Select(value => value.Id).SequenceEqual(["K1", "K2", "K3"]) &&
            results.All(value => value.Status == "passed" && value.Measurements == 1 &&
                value.Errors == 0 && value.OverLimit == 0 && value.MaximumMs == value.LimitMs),
            "Exactly 1000/10000 ms is allowed; diagnostic HTTP, connection, browser and other notifications must not fail business requirements.");
        Require(PerformanceRequirements.Evaluate([]).All(value => value.Status == "not-measured" &&
            value.Measurements == 0 && value.Errors == 0 && value.OverLimit == 0 && value.MaximumMs is null),
            "An unmeasured requirement must never be reported as passed.");
        foreach (var rule in rules)
        {
            var over = PerformanceRequirements.Evaluate([.. boundaries,
                Observation(rule.Category, rule.Operation, rule.Limit + .01)]);
            Require(over.Single(value => value.Id == rule.Id) is { Status: "failed", OverLimit: 1, Errors: 0 } &&
                over.Where(value => value.Id != rule.Id).All(value => value.Status == "passed"),
                $"{rule.Id} must fail for one observation above its maximum, independently of other requirements.");
            var failed = PerformanceRequirements.Evaluate([.. boundaries,
                Observation(rule.Category, rule.Operation, 1, false)]).Single(value => value.Id == rule.Id);
            Require(failed is { Status: "failed", Errors: 1, OverLimit: 0 },
                $"{rule.Id} must fail for a failed operation even when its measured duration is short.");
        }
        foreach (var rule in rules.Where(rule => rule.Id is "K1" or "K3"))
        {
            var cancelled = PerformanceRequirements.Evaluate([.. boundaries,
                Observation("cancelled", rule.Operation, rule.Limit * 2, false)]).Single(value => value.Id == rule.Id);
            Require(cancelled is { Status: "incomplete", Measurements: 1, Cancelled: 1, Errors: 0, OverLimit: 0 },
                $"{rule.Id} cancellation must remain unverified without counting as a server failure or completed measurement.");
        }
        Require(PerformanceRequirements.Evaluate(boundaries, expectedQueueMeasurements: 3)
                .Single(value => value.Id == "K2") is { Status: "incomplete", Measurements: 1, Errors: 0 },
            "One first-queue observation cannot verify three configured lesson queues.");

        var html = HtmlReport.Render(DateTimeOffset.UtcNow, 30, 0, [], observations,
            MetricsCollector.Summarize(observations), new RunStatus("completed", null, true), requirements: results);
        Require(html.Split("1 s (1000 ms)").Length - 1 == 1 && html.Split("10 s (10000 ms)").Length - 1 == 2 &&
            !html.Contains("P3", StringComparison.Ordinal) && html.Contains("visible rendering is optional diagnostics"),
            "Only K1 may draw a one-second requirement line; K2/K3 draw ten seconds, and UI rendering remains optional.");
        string Chart(string title) => html.Split($"<section><h2>{title}</h2>")[1].Split("</section>")[0];
        Require(Chart("K1: HelpRequest registration HTTP").Contains("1 s (1000 ms)") &&
            Chart("K2: First request to teacher notification").Contains("10 s (10000 ms)") &&
            Chart("K3: Teacher answer to student notification").Contains("10 s (10000 ms)"),
            "Requirement graph limits must belong to the matching operation, rather than generic latency graphs.");

        var directory = Path.Combine(output, "historical-requirement-rules");
        await using (var metrics = new MetricsCollector(directory))
        {
            metrics.BeginMeasurement();
            metrics.RecordOperation("http", PerformanceRequirements.CreateOperation, metrics.Capture(), true, 200);
            await metrics.ExportAsync(new { Outcome = "completed", Purpose = "Synthetic historical reporting regression" });
        }
        var rawPath = Path.Combine(directory, "raw.json");
        var historical = JsonNode.Parse(await File.ReadAllTextAsync(rawPath))!.AsObject();
        historical.Remove("RequirementsVersion");
        await File.WriteAllTextAsync(rawPath, historical.ToJsonString());
        await MetricsCollector.RenderSavedReportAsync(directory);
        var historicalHtml = await File.ReadAllTextAsync(Path.Combine(directory, "report.html"));
        Require(historicalHtml.Contains("Historical measurement rules") &&
            !historicalHtml.Contains("<h2>Requirements:") && !historicalHtml.Contains("<h2>K1:") &&
            !historicalHtml.Contains("1 s (1000 ms)") && !historicalHtml.Contains("10 s (10000 ms)"),
            "Saved raw data without the current requirements version must retain historical observations without a new assessment.");
    }

    private static async Task VerifyNotificationBudgetAsync(string output)
    {
        await using var metrics = new MetricsCollector(Path.Combine(output, "notification-budget"));
        metrics.BeginMeasurement();
        var tracker = new NotificationTracker(metrics);
        var sessionId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        using (var first = await tracker.BeginInitialQueueCreationAsync(sessionId, CancellationToken.None)
                   ?? throw new InvalidOperationException("A fresh lesson must grant its initial-queue lease."))
        {
            var competing = tracker.BeginInitialQueueCreationAsync(sessionId, CancellationToken.None);
            Require(!competing.IsCompleted, "Concurrent students must not both become the first request in one queue.");
            var otherLesson = Guid.NewGuid();
            using var independent = await tracker.BeginInitialQueueCreationAsync(otherLesson, CancellationToken.None);
            Require(independent is not null, "Another lesson must grant its initial lease independently.");
            first.Finish();
            first.Dispose();
            Require(await competing.WaitAsync(TimeSpan.FromSeconds(1)) is null &&
                await tracker.BeginInitialQueueCreationAsync(sessionId, CancellationToken.None) is null &&
                tracker.InitialQueueCreationFinished(sessionId) && !tracker.InitialQueueCreationFinished(otherLesson),
                "Finishing a first creation must release waiting students without granting a second first-request probe.");
        }
        var budget = TimeSpan.FromMilliseconds(25);
        using (var minimum = tracker.Expect("notification-deadline-minimum", "student", sessionId, "Created", requestId))
        {
            minimum.MarkHttpSent();
            Require(!await minimum.WaitAsync(budget, CancellationToken.None),
                "A missing message must fail at the deadline.");
        }
        using (var spent = tracker.Expect("http-already-spent-budget", "student", sessionId, "Created", requestId))
        {
            spent.MarkHttpSent();
            await Task.Delay(50);
            var waiting = spent.WaitAsync(budget, CancellationToken.None);
            Require(waiting.IsCompleted && !await waiting,
                "Waiting after HTTP consumed the deadline must finish immediately instead of starting a fresh timeout.");
        }
        using (var late = tracker.Expect("late-already-completed-callback", "student", sessionId, "Created", requestId))
        {
            late.MarkHttpSent();
            await Task.Delay(50);
            tracker.Observe(new NotificationDeliveryRequestedMessage(Guid.NewGuid(), "student", "synthetic",
                DateTimeOffset.UtcNow, sessionId, requestId, 1, "Created"), "student");
            Require(!await late.WaitAsync(budget, CancellationToken.None),
                "A late matching callback must fail even when its completion task finished before WaitAsync was called.");
        }
        using (var anyTeacher = tracker.Expect(PerformanceRequirements.QueueOperation, "teacher-1", sessionId,
                   "Created", eventType: NotificationEventType.HelpRequestCreated,
                   allowedRecipients: ["teacher-1", "teacher-2", "teacher-3"]))
        {
            anyTeacher.MarkHttpSent();
            tracker.Observe(new NotificationDeliveryRequestedMessage(Guid.NewGuid(), "unrelated-user", "synthetic",
                DateTimeOffset.UtcNow, sessionId, requestId, 1, "Created", EventType: NotificationEventType.HelpRequestCreated), "unrelated-user");
            tracker.Observe(new NotificationDeliveryRequestedMessage(Guid.NewGuid(), "teacher-2", "synthetic",
                DateTimeOffset.UtcNow, sessionId, requestId, 1, "Created", EventType: NotificationEventType.HelpRequestCreated), "teacher-2");
            anyTeacher.BindRequest(requestId);
            Require(await anyTeacher.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None),
                "R2 must accept the first matching receipt by another authorized teacher before HTTP reveals the ID.");
        }
        await metrics.ExportAsync(new { Outcome = "completed", Purpose = "Synthetic notification deadline and recipient checks" });
        using var raw = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "notification-budget", "raw.json")));
        Require(raw.RootElement.GetProperty("Operations").EnumerateArray()
                .Single(value => value.GetProperty("Operation").GetString() == "notification-deadline-minimum")
                .GetProperty("DurationMs").GetDouble() >= budget.TotalMilliseconds,
            "Timer rounding must never declare a notification timeout before the monotonic deadline.");
        Require(raw.RootElement.GetProperty("Operations").EnumerateArray()
                .Single(value => value.GetProperty("Operation").GetString() == PerformanceRequirements.QueueOperation)
                .GetProperty("Actor").GetString() == "teacher-2",
            "The first-queue measurement must retain the actual receiving teacher, rather than the primary teacher label.");
    }

    private static async Task VerifyFailedAnswerProbeAsync(string output)
    {
        var directory = Path.Combine(output, "failed-answer-probe");
        await using var metrics = new MetricsCollector(directory);
        metrics.BeginMeasurement();
        var tracker = new NotificationTracker(metrics);
        var sessionId = Guid.NewGuid();
        var failedRequest = Guid.NewGuid();
        using (var failed = tracker.Expect(PerformanceRequirements.AnswerOperation, "student", sessionId,
                   "Answered", failedRequest, NotificationEventType.HelpRequestAnswered))
        {
            failed.MarkHttpSent();
            // Even receipt before an unsuccessful HTTP completion must not turn that action green.
            tracker.Observe(new NotificationDeliveryRequestedMessage(Guid.NewGuid(), "student", "synthetic",
                DateTimeOffset.UtcNow, sessionId, failedRequest, 1, "Answered",
                EventType: NotificationEventType.HelpRequestAnswered), "student");
            failed.RecordHttpFailure("Synthetic answer HTTP failure");
            failed.RecordHttpFailure("Must not record twice");
        }
        var cancelledRequest = Guid.NewGuid();
        using (var cancelled = tracker.Expect(PerformanceRequirements.AnswerOperation, "student", sessionId,
                   "Answered", cancelledRequest, NotificationEventType.HelpRequestAnswered))
        {
            cancelled.MarkHttpSent();
            cancelled.RecordHttpFailure("CallerCancellation", cancelled: true);
        }
        using (var beforeSend = tracker.Expect(PerformanceRequirements.AnswerOperation, "student", sessionId,
                   "Answered", Guid.NewGuid(), NotificationEventType.HelpRequestAnswered))
            beforeSend.RecordHttpFailure("Synthetic failure before HTTP send");

        await metrics.ExportAsync(new { Outcome = "completed", Purpose = "Synthetic failed-answer probe regression" });
        using var raw = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "raw.json")));
        var observations = raw.RootElement.GetProperty("Operations").EnumerateArray().ToArray();
        Require(observations.Length == 2 && observations.All(value => !value.GetProperty("Success").GetBoolean()),
            "An armed failed answer must be recorded once, including when a callback arrived first; failure before send has no measured interval.");
        var failure = observations.Single(value => value.GetProperty("Category").GetString() == "notification");
        Require(failure.GetProperty("Operation").GetString() == PerformanceRequirements.AnswerOperation &&
            failure.GetProperty("CorrelationId").GetString() == failedRequest.ToString() &&
            failure.GetProperty("Actor").GetString() == "student" &&
            failure.GetProperty("Error").GetString() == "Synthetic answer HTTP failure",
            "A failed answer must preserve K3's operation, owner, request ID and failure reason.");
        var cancellation = observations.Single(value => value.GetProperty("Category").GetString() == "cancelled");
        Require(cancellation.GetProperty("CorrelationId").GetString() == cancelledRequest.ToString(),
            "An answer interrupted by caller cancellation must retain its exact request ID separately from transport failure.");
        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "summary.json")));
        var requirement = summary.RootElement.GetProperty("Requirements").EnumerateArray()
            .Single(value => value.GetProperty("Id").GetString() == "K3");
        Require(requirement.GetProperty("Status").GetString() == "failed" &&
            requirement.GetProperty("Measurements").GetInt32() == 1 &&
            requirement.GetProperty("Errors").GetInt32() == 1 &&
            requirement.GetProperty("Cancelled").GetInt32() == 1,
            "A failed answer HTTP request must fail K3; caller cancellation must also remain visible without being counted as a completed measurement.");
    }

    private static async Task VerifyFailureDiagnosticsAsync(string output)
    {
        var directory = Path.Combine(output, "failure-diagnostics");
        await using var metrics = new MetricsCollector(directory);
        metrics.BeginMeasurement();
        metrics.SetPhase("steady-state", 1);

        metrics.ConnectionOpened("planned");
        metrics.ConnectionCloseScheduled("planned");
        // SignalR's Closed callback may have no reason and run before StopAsync returns.
        metrics.ConnectionClosed("planned");
        metrics.ConnectionClosed("planned", "Scheduled disconnect.");
        metrics.ConnectionOpened("unexpected");
        metrics.ConnectionClosed("unexpected", "IOException");
        metrics.ConnectionOpened("recovering");
        metrics.ConnectionReconnecting("recovering", "IOException");
        metrics.ConnectionReconnected("recovering");
        metrics.SetPhase("ramp-down", 0);
        metrics.ConnectionClosed("recovering");
        metrics.ConnectionOpened("unexpected-during-shutdown");
        metrics.ConnectionClosed("unexpected-during-shutdown", "IOException");
        metrics.ConnectionOpened("scheduled-with-transport-failure");
        metrics.ConnectionCloseScheduled("scheduled-with-transport-failure");
        metrics.ConnectionClosed("scheduled-with-transport-failure", "IOException");

        var started = metrics.Capture();
        metrics.RecordOperation("connection", "signalr.connect", started, true,
            completedTimestamp: started.Timestamp + Stopwatch.Frequency);
        metrics.RecordOperation("connection", "signalr.connect", metrics.Capture(), false, error: "IOException");
        metrics.RecordOperation("http", "GET synthetic", started, true, 200,
            completedTimestamp: started.Timestamp + Stopwatch.Frequency);
        metrics.RecordOperation("http", "GET synthetic", metrics.Capture(), false, 503);
        metrics.RecordOperation("notification", "synthetic-timeout", metrics.Capture(), false,
            error: "No matching SignalR notification before timeout.");
        metrics.RecordFailure("student_iteration", "TimeoutException");
        metrics.RecordFailure("synthetic-other", "IOException");
        metrics.RecordOperation("cancelled", "synthetic-stop", metrics.Capture(), false);
        await metrics.ExportAsync(new { Outcome = "completed", Purpose = "Synthetic diagnostics regression only" });

        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "summary.json")));
        var root = summary.RootElement;
        var diagnostics = root.GetProperty("Diagnostics");
        Require(root.GetProperty("TotalFailures").GetInt32() == 5,
            "TotalFailures must retain all failed observations, including the timeout and its failed iteration, while excluding cancellation.");
        Require(diagnostics.GetProperty("ConnectionAttempts").GetInt32() == 2 &&
            diagnostics.GetProperty("ConnectionFailures").GetInt32() == 1 &&
            diagnostics.GetProperty("SuccessfulConnectionsAtOrOverOneSecond").GetInt32() == 1,
            "A slow successful connection must be distinguished from a failed connection.");
        Require(diagnostics.GetProperty("HttpRequests").GetInt32() == 2 &&
            diagnostics.GetProperty("HttpFailures").GetInt32() == 1 &&
            diagnostics.GetProperty("NotificationFailures").GetInt32() == 1 &&
            diagnostics.GetProperty("IterationFailures").GetInt32() == 1 &&
            diagnostics.GetProperty("OtherFailedObservations").GetInt32() == 1,
            "Transport, delivery, iteration and other failures must be counted separately without hiding observations.");
        Require(diagnostics.GetProperty("PlannedConnectionClosures").GetInt32() == 1 &&
            diagnostics.GetProperty("UnclassifiedShutdownClosures").GetInt32() == 1 &&
            diagnostics.GetProperty("UnexpectedConnectionClosures").GetInt32() == 3 &&
            diagnostics.GetProperty("ReconnectingEvents").GetInt32() == 1 &&
            diagnostics.GetProperty("ReconnectedEvents").GetInt32() == 1,
            "An explicitly scheduled close must survive the Closed callback race, while failures during shutdown and unclassified older closes remain distinct.");
        var originalHtml = await File.ReadAllTextAsync(Path.Combine(directory, "report.html"));
        Require(originalHtml.Contains("not independent incidents") && originalHtml.Contains("Failed scenario iterations: 1") &&
            originalHtml.Contains("successful deliveries") && originalHtml.Contains("Failed notification waits"),
            "The report must explain overlapping failure observations and separate successful delivery latency from notification deadlines.");
        await MetricsCollector.RenderSavedReportAsync(directory);
        Require(await File.ReadAllTextAsync(Path.Combine(directory, "report.html")) == originalHtml,
            "Regenerating a saved report must preserve connection classifications and failure diagnostics.");
    }

    private static async Task VerifyStartupReportingAsync(string output)
    {
        const string failure = "Synthetic teacher login rejection <untrusted>";
        var failedOutput = Path.Combine(output, "startup-failure");
        await using (var startup = new MetricsCollector(failedOutput))
            await startup.ExportAsync(new { Outcome = "failed", Failure = failure });
        using (var raw = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(failedOutput, "raw.json"))))
            Require(!raw.RootElement.GetProperty("RunStatus").GetProperty("MeasurementStarted").GetBoolean(),
                "A startup failure must be persisted independently from zero measured operation failures.");

        var failedHtml = await File.ReadAllTextAsync(Path.Combine(failedOutput, "report.html"));
        Require(failedHtml.Contains("Run failed") && failedHtml.Contains("No measured load was recorded.") &&
            failedHtml.Contains("&lt;untrusted&gt;") && !failedHtml.Contains("0001-01-01") && !failedHtml.Contains("<svg"),
            "A startup failure must explain the escaped failure and absent measurements instead of showing empty charts.");

        // Older runs saved the failure only in summary.Configuration, including the reported
        // OIDC startup error. Removing the new field exercises that existing on-disk format.
        var legacyRaw = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(failedOutput, "raw.json")))!.AsObject();
        legacyRaw.Remove("RunStatus");
        await File.WriteAllTextAsync(Path.Combine(failedOutput, "raw.json"), legacyRaw.ToJsonString());
        await MetricsCollector.RenderSavedReportAsync(failedOutput);
        Require(await File.ReadAllTextAsync(Path.Combine(failedOutput, "report.html")) == failedHtml,
            "Rerendering an older raw file must recover its original failure from the saved summary.");

        foreach (var outcome in new[] { "preflight-only", "teacher-preflight-only", "cancelled" })
        {
            var html = HtmlReport.Render(default, 0, 0, [], [], [], new RunStatus(outcome, null, false));
            Require(html.Contains($"<strong>{outcome}</strong>") && html.Contains("No measured load was recorded.") &&
                !html.Contains("<svg") && !html.Contains("Load run completed"),
                "Preflight and cancellation before measurement must not appear to be a completed performance run.");
        }
        var partial = HtmlReport.Render(DateTimeOffset.UtcNow, 1, 0, [], [], [], new RunStatus("failed", failure, true));
        Require(partial.Contains("Only partial load measurements are available") && partial.Contains("<svg"),
            "A failure after measurement starts must retain observed graphs and mark the run incomplete.");
    }

    private static async Task VerifyIdentityCookieIsolationAsync()
    {
        using var probe = new CookieProbeTransport();
        using var transport = new HttpMessageInvoker(probe, disposeHandler: false);
        var firstCookies = new System.Net.CookieContainer();
        var secondCookies = new System.Net.CookieContainer();
        using var first = new HttpClient(new IdentityCookieHandler(firstCookies, transport));
        using var second = new HttpClient(new IdentityCookieHandler(secondCookies, transport));
        first.DefaultRequestHeaders.Authorization = new("Bearer", "first-token");
        second.DefaultRequestHeaders.Authorization = new("Bearer", "second-token");

        await Task.WhenAll(first.GetAsync("http://performance.invalid/api/first/login"),
            second.GetAsync("http://performance.invalid/api/second/login"));
        Require(firstCookies.GetCookies(new Uri("http://performance.invalid/api/"))["identity"]?.Value == "first" &&
            secondCookies.GetCookies(new Uri("http://performance.invalid/api/"))["identity"]?.Value == "second",
            "Set-Cookie on a shared transport must update only the receiving identity's CookieContainer.");
        await Task.WhenAll(first.GetAsync("http://performance.invalid/api/first/check"),
            second.GetAsync("http://performance.invalid/api/second/check"));
        Require(probe.Received["first"] == ("identity=first", "first-token") &&
            probe.Received["second"] == ("identity=second", "second-token"),
            "Sharing connections must never share student cookies or teacher Authorization headers.");

        first.Dispose();
        using var surviving = await second.GetAsync("http://performance.invalid/api/second/check");
        Require(!probe.Disposed && surviving.IsSuccessStatusCode,
            "Disposing one identity must leave the shared transport and every other identity usable.");
    }

    private sealed class CookieProbeTransport : HttpMessageHandler
    {
        public System.Collections.Concurrent.ConcurrentDictionary<string, (string Cookie, string? Token)> Received { get; } = new();
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var parts = request.RequestUri!.AbsolutePath.Split('/');
            var identity = parts[2];
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            if (parts[3] == "login")
                response.Headers.TryAddWithoutValidation("Set-Cookie", $"identity={identity}; Path=/api; HttpOnly");
            else
                Received[identity] = (string.Join("; ", request.Headers.GetValues("Cookie")),
                    request.Headers.Authorization?.Parameter);
            return Task.FromResult(response);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Harness self-check failed: {message}");
    }

    private static string FindWorkspace()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (Directory.Exists(Path.Combine(directory.FullName, ".git"))) return directory.FullName;
        throw new InvalidOperationException("Run the synthetic self-check inside the Bifrost workspace.");
    }
}
