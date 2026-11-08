using System.Runtime.InteropServices;
using System.Text.Json;
using Backend.Performance.Tests.Configuration;
using Backend.Performance.Tests.Eir;
using Backend.Performance.Tests.Infrastructure;
using Backend.Performance.Tests.Provisioning;
using Backend.Performance.Tests.Reporting;

if (args.Contains("--help"))
{
    Console.WriteLine("Backend.Performance.Tests [--smoke | --config path] [--check | --check-teachers]\n" +
        "  --check: real gateway student identity preflight only\n" +
        "  --check-teachers: student preflight and thirty distinct authenticated teachers; no load or test data\n" +
        "  --self-test: offline correlation/reporting checks (no performance results)\n" +
        "  --render-report directory: rebuild HTML graphs from saved raw.json without backend access\n" +
        "  --provision-teachers / --delete-provisioned-teachers: administer thirty owned Var test accounts\n" +
        "Credentials: thirty BIFROST_PERF_TEACHER_{1..30}_JWT variables OR _USERNAME and _PASSWORD pairs.\n" +
        "Gateway override: BIFROST_PERF_GATEWAY_URL. See README.md for setup and cleanup.");
    return 0;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
try
{
    if (args.Contains("--self-test")) { await SelfCheck.RunAsync(); return 0; }
    var renderIndex = Array.IndexOf(args, "--render-report");
    if (renderIndex >= 0)
    {
        if (renderIndex + 1 >= args.Length) throw new ArgumentException("--render-report requires a result directory.");
        await MetricsCollector.RenderSavedReportAsync(args[renderIndex + 1]);
        Console.WriteLine($"Report regenerated from observed raw data: {Path.GetFullPath(args[renderIndex + 1])}");
        return 0;
    }
    if (args.Contains("--provision-teachers"))
    { await TeacherProvisioner.ProvisionAsync(cancellation.Token); Console.WriteLine("Thirty owned VarService performance teachers verified."); return 0; }
    if (args.Contains("--delete-provisioned-teachers"))
    { await TeacherProvisioner.DeleteAsync(cancellation.Token); Console.WriteLine("Thirty owned VarService performance teachers deleted and verified."); return 0; }
    var configIndex = Array.IndexOf(args, "--config");
    if (configIndex >= 0 && configIndex + 1 >= args.Length) throw new ArgumentException("--config requires a file path.");
    var configPath = configIndex >= 0 ? args[configIndex + 1] : args.Contains("--smoke")
        ? Path.Combine(AppContext.BaseDirectory, "Configuration", "smoke.json") : null;
    var options = PerformanceOptions.Load(configPath);
    var runId = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N")[..6];
    var directory = Path.GetFullPath(Path.Combine(options.OutputDirectory, runId));
    Directory.CreateDirectory(directory);
    Console.WriteLine($"Run: {runId}\nArtifacts: {directory}");
    await using var metrics = new MetricsCollector(directory, options.ResourceProcessIds);
    metrics.StartSampling();
    var outcome = "failed";
    string? failure = null;
    try
    {
        // A single real, unauthenticated request exercises the actual student bootstrap before
        // creating data. Failure here is a failed preflight, never a passing performance run.
        try
        {
            using var preflight = new GatewayClient(options.GatewayUrl, metrics, TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
            var first = await preflight.AuthenticateStudentAsync(cancellation.Token);
            var second = await preflight.AuthenticateStudentAsync(cancellation.Token);
            if (first.UserId != second.UserId) throw new InvalidOperationException("Student cookie identity was not stable.");
        }
        catch (HttpRequestException error) when (error.InnerException is System.Net.Sockets.SocketException
            { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused })
        {
            throw new InvalidOperationException($"Gateway {options.GatewayUrl.GetLeftPart(UriPartial.Authority)} is not accepting connections. " +
                "Start Docker Desktop and Yggdrasil/Aspire with scripts/Test/Perfomance/start-aspire.cmd, " +
                "then run check-teachers.cmd before the load test.", error);
        }
        Console.WriteLine("Gateway student identity/cookie preflight passed.");
        if (args.Contains("--check-teachers"))
        {
            GatewayClient[] teachers = [];
            try
            {
                var tokens = options.TeacherTokenEnvironmentVariables
                    .Select(name => Environment.GetEnvironmentVariable(name) ?? "").ToArray();
                teachers = tokens.All(token => !string.IsNullOrWhiteSpace(token))
                    ? tokens.Select(token => new GatewayClient(options.GatewayUrl, metrics,
                        TimeSpan.FromSeconds(options.RequestTimeoutSeconds), jwt: token)).ToArray()
                    : await GatewayTeacherLogin.LoginAllAsync(options.GatewayUrl, metrics,
                        TimeSpan.FromSeconds(options.RequestTimeoutSeconds), cancellation.Token);
                foreach (var teacher in teachers) await teacher.AuthenticateTeacherAsync(cancellation.Token);
                if (teachers.Select(teacher => teacher.UserId).Distinct(StringComparer.Ordinal).Count() != 30)
                    throw new InvalidOperationException("Teacher preflight requires thirty separate authenticated teacher users.");
                Console.WriteLine("Thirty distinct teacher identities and gateway rights validated; no load was started.");
                outcome = "teacher-preflight-only";
                return 0;
            }
            finally { foreach (var teacher in teachers) teacher.Dispose(); }
        }
        if (args.Contains("--check")) { outcome = "preflight-only"; return 0; }
        Console.WriteLine($"Load: {options.Students} students ({options.ActiveStudents} active, {options.Students - options.ActiveStudents} listening), " +
            $"30 teachers (3 per session), {options.Lessons} sessions, max {options.MaxRequestsPerActiveStudent} help request per active student.");
        Console.WriteLine("K1 measures HelpRequest HTTP; K2 measures teacher receipt; K3 measures teacher answer to student receipt. No polling or student reloads are run.");
        await new LoadTest(options, metrics, directory).RunAsync(cancellation.Token);
        outcome = "completed";
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        outcome = "cancelled";
        failure = "Cancelled by the operator; cleanup was attempted.";
        throw;
    }
    catch (Exception error)
    {
        failure = $"{error.GetType().Name}: {error.Message}";
        throw;
    }
    finally
    {
        await metrics.ExportAsync(new
        {
            RunId = runId, Outcome = outcome, Failure = failure, Options = options,
            Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            HarnessAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(typeof(LoadTest).Assembly.Location))),
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount, GeneratorHost = Environment.MachineName,
            RequirementScope = "K1: registration HTTP <=1s; K2: first request per fresh queue to a teacher notification <=10s; K3: teacher answer HTTP send to the requesting student's HelpRequestAnswered notification <=10s. No polling or student reloads are measured.",
            ServerConnectionRegistry = "No public inspection endpoint exists; client closure verified, server cleanup needs exported telemetry",
            TestDataCleanupScope = "Session DELETE/detail/list verified. Existing backend deletion handlers leave help requests/messages/participants/FAQ data; full database cleanup is not implemented in the public flow. Use an isolated disposable test dataset."
        });
        Console.WriteLine($"Report: {Path.Combine(directory, "report.html")}");
    }
    using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "summary.json")));
    Console.WriteLine("Measured requirements (maximum per action; not NBomber iteration latency):");
    foreach (var requirement in summary.RootElement.GetProperty("Requirements").EnumerateArray())
    {
        var maximum = requirement.GetProperty("MaximumMs");
        var maximumText = maximum.ValueKind == JsonValueKind.Null ? "n/a" : $"{maximum.GetDouble():0.##} ms";
        Console.WriteLine($"  {requirement.GetProperty("Id").GetString()}: {requirement.GetProperty("Status").GetString()}, N={requirement.GetProperty("Measurements").GetInt32()}, max={maximumText}, limit={requirement.GetProperty("LimitMs").GetDouble():0} ms, errors={requirement.GetProperty("Errors").GetInt32()}, over limit={requirement.GetProperty("OverLimit").GetInt32()}, cancelled={requirement.GetProperty("Cancelled").GetInt32()}");
    }
    Console.WriteLine($"HTTP: {summary.RootElement.GetProperty("HttpRequests").GetInt64()} completed, {summary.RootElement.GetProperty("HttpFailures").GetInt64()} failed.");
    var hasFailures = summary.RootElement.GetProperty("Operations").EnumerateArray()
        .Any(operation => operation.GetProperty("Category").GetString() != "cancelled" &&
            operation.GetProperty("Failures").GetInt64() > 0);
    hasFailures |= summary.RootElement.GetProperty("Requirements").EnumerateArray()
        .Any(requirement => requirement.GetProperty("Status").GetString() != "passed");
    Console.WriteLine(hasFailures ? "Completed; requirements failed or were not all measured, or operational errors occurred. Inspect summary.json and report.html." : "Completed; all three measured requirements passed. Inspect sample counts and report.html.");
    return hasFailures ? 1 : 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{ Console.Error.WriteLine("Cancelled; cleanup was attempted and available measurements exported."); return 130; }
catch (Exception error)
{
    Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}");
    return 1;
}
