using System.Text.Json;

namespace Backend.Performance.Tests.Configuration;

public sealed record PerformanceOptions
{
    public Uri GatewayUrl { get; init; } = new("http://localhost:6004");
    public int Students { get; init; } = 1285;
    public int ActiveStudents { get; init; } = 129;
    public int WarmupStudents { get; init; } = 10;
    public int Lessons { get; init; } = 10;
    public int[] LessonWeights { get; init; } = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1];
    public double WarmupSeconds { get; init; } = 60;
    public double RampUpSeconds { get; init; } = 300;
    public double SteadySeconds { get; init; } = 600;
    public double RampDownSeconds { get; init; } = 60;
    public double StudentPauseSeconds { get; init; } = 15;
    public double RequestStartSpreadSeconds { get; init; }
    public double TeacherPauseSeconds { get; init; } = 2;
    public int MaxRequestsPerActiveStudent { get; init; } = 1;
    public double PauseJitterFraction { get; init; } = 0.2;
    // Compatibility with configuration files: the business requirement is fixed at ten seconds.
    public double NotificationTimeoutSeconds { get; init; } = 10;
    public double RequestTimeoutSeconds { get; init; } = 30;
    public double DigitalRequestFraction { get; init; } = 1;
    public double CancelRequestFraction { get; init; } = 0.05;
    public double FollowUpFraction { get; init; } = 0.1;
    public double FaqAcceptFraction { get; init; } = 0.8;
    public string[] TeacherTokenEnvironmentVariables { get; init; } =
        Enumerable.Range(1, 30).Select(number => $"BIFROST_PERF_TEACHER_{number}_JWT").ToArray();
    public int[] ResourceProcessIds { get; init; } = [];
    public int BrowserClients { get; init; }
    public Uri? FrontendUrl { get; init; }
    public string OutputDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "reports");
    public string EnvironmentDescription { get; init; } = "UNSPECIFIED: record host/container, CPU/RAM limits, network and LLM mode before a full run";

    public static PerformanceOptions Load(string? path)
    {
        var result = path is null ? new() : JsonSerializer.Deserialize<PerformanceOptions>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new ArgumentException("Empty configuration.");
        var gateway = Environment.GetEnvironmentVariable("BIFROST_PERF_GATEWAY_URL");
        if (!string.IsNullOrWhiteSpace(gateway)) result = result with { GatewayUrl = new Uri(gateway) };
        result.Validate();
        return result;
    }

    public void Validate()
    {
        if (!GatewayUrl.IsAbsoluteUri || GatewayUrl.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(GatewayUrl.UserInfo) || !string.IsNullOrEmpty(GatewayUrl.Query))
            throw new ArgumentException("GatewayUrl must be an HTTP(S) URL without credentials/query.");
        if (Students < 1 || ActiveStudents < 1 || ActiveStudents > Students || WarmupStudents < 1 || Lessons is < 1 or > 10)
            throw new ArgumentException("Invalid student/lesson counts.");
        if (LessonWeights.Length != Lessons || LessonWeights.Any(x => x <= 0))
            throw new ArgumentException("One positive LessonWeight per lesson is required.");
        foreach (var value in new[] { WarmupSeconds, RampUpSeconds, SteadySeconds, RampDownSeconds,
                     StudentPauseSeconds, TeacherPauseSeconds, NotificationTimeoutSeconds, RequestTimeoutSeconds })
            if (!double.IsFinite(value) || value < 1) throw new ArgumentException("Durations and pauses must be at least one second.");
        if (!double.IsFinite(RequestStartSpreadSeconds) || RequestStartSpreadSeconds < 0)
            throw new ArgumentException("RequestStartSpreadSeconds must be zero or greater.");
        if (MaxRequestsPerActiveStudent is < 1 or > 10)
            throw new ArgumentException("MaxRequestsPerActiveStudent must be between 1 and 10.");
        if (NotificationTimeoutSeconds != 10)
            throw new ArgumentException("NotificationTimeoutSeconds must be 10: R2/R3 have a fixed ten-second deadline from HTTP send.");
        foreach (var fraction in new[] { DigitalRequestFraction, CancelRequestFraction, FollowUpFraction, FaqAcceptFraction, PauseJitterFraction })
            if (!double.IsFinite(fraction) || fraction is < 0 or > 1) throw new ArgumentException("Fractions must be in [0,1].");
        if (TeacherTokenEnvironmentVariables.Length != 30 || TeacherTokenEnvironmentVariables.Distinct().Count() != 30)
            throw new ArgumentException("Exactly thirty distinct teacher JWT environment variable names are required.");
        if (BrowserClients is < 0 or > 5 || BrowserClients > 0 && FrontendUrl is null)
            throw new ArgumentException("BrowserClients must be 0..5; FrontendUrl is required when enabled.");
    }

    public int LessonFor(int student)
    {
        var slot = student % LessonWeights.Sum();
        for (var lesson = 0; lesson < LessonWeights.Length; lesson++)
        {
            if (slot < LessonWeights[lesson]) return lesson;
            slot -= LessonWeights[lesson];
        }
        throw new InvalidOperationException();
    }

    public TimeSpan Pause(bool teacher) => TimeSpan.FromSeconds((teacher ? TeacherPauseSeconds : StudentPauseSeconds)
        * (1 + (Random.Shared.NextDouble() * 2 - 1) * PauseJitterFraction));
}
