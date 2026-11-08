using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Backend.Performance.Tests.Configuration;
using Backend.Performance.Tests.Infrastructure;
using Backend.Performance.Tests.Reporting;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Backend.Performance.Tests.Eir;

public sealed class LoadTest(PerformanceOptions options, MetricsCollector metrics, string runDirectory)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        GatewayClient[] teacherClients = [];
        try
        {
            var tokens = options.TeacherTokenEnvironmentVariables
                .Select(name => Environment.GetEnvironmentVariable(name) ?? "").ToArray();
            teacherClients = tokens.All(token => !string.IsNullOrWhiteSpace(token))
                ? tokens.Select(token => new GatewayClient(options.GatewayUrl, metrics,
                    TimeSpan.FromSeconds(options.RequestTimeoutSeconds), jwt: token)).ToArray()
                : await GatewayTeacherLogin.LoginAllAsync(options.GatewayUrl, metrics,
                    TimeSpan.FromSeconds(options.RequestTimeoutSeconds), cancellationToken);
            // Session setup still revalidates each user's JWT/rights. Reuse the issued login
            // across phases instead of performing six separate interactive sign-ins.
            await RunSessionAsync(warmup: true, teacherClients, cancellationToken);
            if (metrics.ActualConnections != 0) throw new InvalidOperationException("Warm-up connections did not close.");
            await RunSessionAsync(warmup: false, teacherClients, cancellationToken);
        }
        finally { foreach (var teacher in teacherClients) teacher.Dispose(); }
    }

    private async Task RunSessionAsync(bool warmup, GatewayClient[] teacherClients, CancellationToken cancellationToken)
    {
        var phaseName = warmup ? "warmup" : "measured";
        Console.WriteLine($"Preparing {phaseName} sessions via gateway {options.GatewayUrl}.");
        var runId = $"{Path.GetFileName(runDirectory)}-{phaseName}";
        await using var environment = await TestEnvironment.CreateAsync(options.GatewayUrl, runId, options.Lessons,
            teacherClients, metrics, cancellationToken, disposeTeachers: false);
        await File.WriteAllTextAsync(Path.Combine(runDirectory, $"{phaseName}-sessions.json"),
            JsonSerializer.Serialize(environment.Sessions.Select(x => new { x.Id, x.Title }),
                new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        var studentCount = warmup ? options.WarmupStudents : options.Students;
        var activeCount = warmup ? studentCount : options.ActiveStudents;
        var activeLessonIds = Enumerable.Range(0, activeCount).Select(options.LessonFor).Distinct()
            .Select(index => environment.Sessions[index].Id).ToArray();
        var tracker = new NotificationTracker(metrics);
        var students = new ConcurrentDictionary<int, Lazy<Task<StudentSession>>>();
        var studentClients = new ConcurrentBag<GatewayClient>();
        var identities = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var teachers = new ConcurrentDictionary<int, Lazy<Task<TeacherSession>>>();
        var retired = new ConcurrentDictionary<int, byte>();
        var retiredTeachers = new ConcurrentDictionary<int, byte>();
        var retirementTasks = new List<Task>();
        var runClock = new Stopwatch();
        using var monitorStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var browserStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task monitor = Task.CompletedTask;
        Task browserStartup = Task.CompletedTask;
        Task browserShutdown = Task.CompletedTask;
        BrowserProbe? browsers = null;
        var browserStarted = false;
        var browserStopped = false;
        var audited = false;
        var failures = 0;
        var failureDetails = new ConcurrentQueue<string>();
        var stopRequested = 0;
        var runStarted = 0;
        var startSync = new object();

        void StartRun()
        {
            lock (startSync)
            {
                if (runStarted != 0) return;
                runStarted = 1;
                runClock.Start();
                metrics.SetPhase(warmup ? "warmup" : "ramp-up", warmup ? studentCount + 30 : 30);
                if (!warmup) metrics.BeginMeasurement();
                monitor = MonitorAsync();
            }
        }

        void RequestStop(IScenarioContext context)
        {
            if (Interlocked.Exchange(ref stopRequested, 1) == 0)
                context.StopCurrentTest("The load-test cancellation token was cancelled.");
        }

        void AuditStudents()
        {
            if (audited) return;
            audited = true;
            var connectedCount = students.Values.Count(actor => actor.IsValueCreated &&
                actor.Value.IsCompletedSuccessfully && actor.Value.Result.IsConnected);
            if (identities.Count != studentCount || connectedCount != studentCount)
            {
                Interlocked.Increment(ref failures);
                var detail = $"Expected {studentCount} unique connected students; got {identities.Count} identities and {connectedCount} connections.";
                failureDetails.Enqueue($"student_population: {detail}");
                metrics.RecordFailure("student_population", detail);
            }
        }

        async Task StartBrowsersAsync()
        {
            try { await browsers!.StartAsync(browserStop.Token); }
            catch (OperationCanceledException) when (browserStop.IsCancellationRequested) { }
            catch (Exception error)
            {
                Interlocked.Increment(ref failures);
                failureDetails.Enqueue($"browser_start: {error.GetType().Name}: {error.Message}");
                metrics.RecordFailure("browser_start", error.GetType().Name);
            }
        }

        async Task StopBrowsersAsync()
        {
            try
            {
                await browserStop.CancelAsync();
                await browserStartup;
                if (browsers is not null) await browsers.DisposeAsync();
            }
            catch (Exception error)
            {
                Interlocked.Increment(ref failures);
                failureDetails.Enqueue($"browser_cleanup: {error.GetType().Name}: {error.Message}");
                metrics.RecordFailure("browser_cleanup", error.GetType().Name);
            }
        }

        async Task<StudentSession> NewStudent(int index, CancellationToken ct)
        {
            var client = new GatewayClient(options.GatewayUrl, metrics, TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
            studentClients.Add(client);
            var lesson = environment.Sessions[options.LessonFor(index)];
            var actor = new StudentSession(index, client, lesson,
                options, metrics, tracker, index < activeCount, environment.TeacherUserIdsFor(lesson.Id));
            try
            {
                await actor.ConnectAsync(ct);
                if (!identities.TryAdd(actor.UserId, index))
                    throw new InvalidOperationException("The gateway returned the same student identity to separate cookie containers.");
                if (retired.ContainsKey(index)) await actor.DisposeAsync();
                return actor;
            }
            catch { await actor.DisposeAsync(); throw; }
        }

        var studentScenario = Scenario.Create("students", async context =>
        {
            StartRun();
            using var iteration = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, context.ScenarioCancellationToken);
            var ct = iteration.Token;
            var index = context.ScenarioInfo.InstanceNumber;
            using var cancellationRegistration = cancellationToken.Register(() => RequestStop(context));
            try
            {
                if (!retired.ContainsKey(index))
                {
                    var actor = await students.GetOrAdd(index, i => new Lazy<Task<StudentSession>>(() => NewStudent(i, ct))).Value;
                    var seconds = runClock.Elapsed.TotalSeconds;
                    if (!retired.ContainsKey(index) && (warmup || seconds >= options.RampUpSeconds &&
                        seconds < options.RampUpSeconds + options.SteadySeconds))
                        await actor.TickAsync(ct);
                }
                await Task.Delay(options.Pause(teacher: false), ct);
                return Response.Ok();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || retired.ContainsKey(index)) { return Response.Ok(); }
            catch (Exception error)
            {
                metrics.RecordFailure("student_iteration", $"{error.GetType().Name}: {error.Message}", $"student:{index}");
                failureDetails.Enqueue($"student-{index}: {error.GetType().Name}: {error.Message}");
                Interlocked.Increment(ref failures);
                try { await Task.Delay(options.Pause(teacher: false), ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                return Response.Fail();
            }
        }).WithoutWarmUp().WithLoadSimulations(warmup
            ? [Simulation.KeepConstant(studentCount, TimeSpan.FromSeconds(options.WarmupSeconds))]
            : [Simulation.RampingConstant(studentCount, TimeSpan.FromSeconds(options.RampUpSeconds)),
               Simulation.KeepConstant(studentCount, TimeSpan.FromSeconds(options.SteadySeconds)),
               Simulation.RampingConstant(0, TimeSpan.FromSeconds(options.RampDownSeconds))]);

        var totalSeconds = warmup ? options.WarmupSeconds : options.RampUpSeconds + options.SteadySeconds + options.RampDownSeconds;
        var teacherScenario = Scenario.Create("teachers", async context =>
        {
            StartRun();
            var index = context.ScenarioInfo.InstanceNumber;
            using var iteration = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.ScenarioCancellationToken);
            var ct = iteration.Token;
            using var cancellationRegistration = cancellationToken.Register(() => RequestStop(context));
            try
            {
                if (retiredTeachers.ContainsKey(index))
                {
                    await Task.Delay(options.Pause(teacher: true), ct);
                    return Response.Ok();
                }
                var teacher = await teachers.GetOrAdd(index, i => new Lazy<Task<TeacherSession>>(async () =>
                {
                    var ownedSessions = environment.Sessions
                        .Where(session => environment.IsSessionOwner(session.Id, environment.Teachers[i].UserId!))
                        .Select(session => session.Id)
                        .ToHashSet();
                    var actor = new TeacherSession(i, environment.Teachers[i], environment.Sessions, options,
                        metrics, tracker, ownedSessions);
                    try { await actor.ConnectAsync(ct); return actor; }
                    catch { await actor.DisposeAsync(); throw; }
                })).Value;
                var seconds = runClock.Elapsed.TotalSeconds;
                if (warmup || seconds >= options.RampUpSeconds && seconds < options.RampUpSeconds + options.SteadySeconds)
                    await teacher.TickAsync(ct);
                await Task.Delay(options.Pause(teacher: true), ct);
                return Response.Ok();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || retiredTeachers.ContainsKey(index)) { return Response.Ok(); }
            catch (Exception error)
            {
                metrics.RecordFailure("teacher_iteration", $"{error.GetType().Name}: {error.Message}", $"teacher:{index}");
                failureDetails.Enqueue($"teacher-{index}: {error.GetType().Name}: {error.Message}");
                Interlocked.Increment(ref failures);
                try { await Task.Delay(options.Pause(teacher: true), ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                return Response.Fail();
            }
        }).WithoutWarmUp().WithLoadSimulations(Simulation.KeepConstant(30, TimeSpan.FromSeconds(totalSeconds)));

        async Task MonitorAsync()
        {
            if (warmup) return;
            try
            {
                while (!monitorStop.IsCancellationRequested)
                {
                    var seconds = runClock.Elapsed.TotalSeconds;
                    var rampDownAt = options.RampUpSeconds + options.SteadySeconds;
                    if (seconds < options.RampUpSeconds)
                        metrics.SetPhase("ramp-up", (int)(studentCount * seconds / options.RampUpSeconds) + 30);
                    else if (seconds < rampDownAt)
                    {
                        metrics.SetPhase("steady-state", studentCount + 30 + options.BrowserClients);
                        // Browser submissions must not race the guaranteed first-queue probes.
                        if (!browserStarted && options.BrowserClients > 0 &&
                            activeLessonIds.All(tracker.InitialQueueCreationFinished))
                        {
                            browserStarted = true;
                            browsers = new BrowserProbe(options, metrics, environment, tracker, runDirectory);
                            browserStartup = StartBrowsersAsync();
                        }
                    }
                    else
                    {
                        AuditStudents();
                        if (browsers is not null && !browserStopped)
                        {
                            browserStopped = true;
                            browserShutdown = StopBrowsersAsync();
                        }
                        var remaining = Math.Max(0, (int)Math.Ceiling(studentCount * (1 - (seconds - rampDownAt) / options.RampDownSeconds)));
                        var remainingTeachers = Math.Clamp((int)Math.Ceiling(30 * (1 - (seconds - rampDownAt) / options.RampDownSeconds)), 0, 30);
                        metrics.SetPhase("ramp-down", remaining + remainingTeachers);
                        // Keep all connections alive through ramp-down. The drain phase below
                        // must be able to deliver the final answer notifications to students.
                    }
                    await Task.Delay(100, monitorStop.Token);
                }
            }
            catch (OperationCanceledException) when (monitorStop.IsCancellationRequested) { }
        }

        try
        {
            var runner = NBomberRunner.RegisterScenarios(studentScenario, teacherScenario)
                .WithTestSuite("Bifrost Eir gateway").WithTestName(phaseName)
                .WithReportFolder(Path.Combine(runDirectory, warmup ? "warmup-nbomber" : "nbomber"))
                .WithScenarioCompletionTimeout(TimeSpan.FromSeconds(options.RequestTimeoutSeconds + options.NotificationTimeoutSeconds + 10));
            if (warmup) runner = runner.WithoutReports();
            var license = Environment.GetEnvironmentVariable("NBOMBER_LICENSE");
            if (!string.IsNullOrWhiteSpace(license)) runner = runner.WithLicense(license);
            await Task.Run(() => runner.Run(), CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            if (warmup) AuditStudents();
            if (!warmup && !audited)
                throw new InvalidOperationException("NBomber ended before completing steady-state and auditing the student population.");
            if (!warmup)
            {
                var expectedRequests = activeCount * options.MaxRequestsPerActiveStudent;
                await tracker.WaitForCreatedRequestsAsync(expectedRequests,
                    TimeSpan.FromSeconds(options.RequestTimeoutSeconds), cancellationToken);
                var teacherSessions = await Task.WhenAll(teachers.Values
                    .Where(x => x.IsValueCreated)
                    .Select(x => x.Value));
                var drainDeadline = DateTime.UtcNow.AddSeconds(options.NotificationTimeoutSeconds * 2);
                while (DateTime.UtcNow < drainDeadline && tracker.AnswerNotificationCount < expectedRequests)
                {
                    await Task.WhenAll(teacherSessions.Select(teacher => teacher.DrainAnswersAsync(cancellationToken)));
                    if (tracker.AnswerNotificationCount >= expectedRequests) break;
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                }
                await tracker.WaitForAllAnswerNotificationsAsync(expectedRequests,
                    TimeSpan.FromSeconds(1), cancellationToken);
            }
            if (warmup && failures > 0)
            {
                var details = string.Join(" | ", failureDetails.Take(8));
                throw new InvalidOperationException(
                    $"Warm-up had {failures} failures; the measured run was not started. Details: {details}");
            }
        }
        finally
        {
            monitorStop.Cancel();
            try { await monitor; }
            catch (Exception error) { metrics.RecordFailure("phase_monitor", error.GetType().Name); }
            await Task.WhenAll(retirementTasks);
            if (browsers is not null)
            {
                if (!browserStopped) browserShutdown = StopBrowsersAsync();
                await browserShutdown;
            }
            foreach (var actor in students.Values.Where(x => x.IsValueCreated))
                try { await (await actor.Value).DisposeAsync(); }
                catch (Exception error) { metrics.RecordFailure("student_cleanup", error.GetType().Name); }
            foreach (var actor in teachers.Values.Where(x => x.IsValueCreated))
                try { await (await actor.Value).DisposeAsync(); }
                catch (Exception error) { metrics.RecordFailure("teacher_cleanup", error.GetType().Name); }
            foreach (var client in studentClients) client.Dispose();
            await File.WriteAllTextAsync(Path.Combine(runDirectory, $"{phaseName}-students.json"),
                JsonSerializer.Serialize(identities.OrderBy(pair => pair.Value)
                    .Select(pair => new { Student = pair.Value, UserId = pair.Key }),
                    new JsonSerializerOptions { WriteIndented = true }));
            metrics.SetPhase("cleanup", 0);
            if (metrics.ActualConnections != 0)
                throw new InvalidOperationException($"{metrics.ActualConnections} client connections remain after cleanup.");
        }
    }
}
