using System.Net;
using Backend.Performance.Tests.Reporting;
using EirService.Sessions.Application.Features.Teacher.Common;

namespace Backend.Performance.Tests.Infrastructure;

/// <summary>Creates only run-owned sessions through the authenticated public gateway API.</summary>
public sealed class TestEnvironment : IAsyncDisposable
{
    private readonly MetricsCollector metrics;
    private readonly bool disposeTeachers;
    private readonly List<GatewayClient> teachers = [];
    private readonly List<SessionResult> sessions = [];
    private readonly Dictionary<Guid, GatewayClient> sessionOwners = [];
    private readonly Dictionary<Guid, string[]> sessionTeacherUserIds = [];
    private readonly HashSet<Guid> deleted = [];
    private bool disposed;

    private TestEnvironment(MetricsCollector metrics, bool disposeTeachers = true)
    { this.metrics = metrics; this.disposeTeachers = disposeTeachers; }

    public IReadOnlyList<GatewayClient> Teachers => teachers;
    public IReadOnlyList<SessionResult> Sessions => sessions;
    public IReadOnlyList<string> TeacherUserIdsFor(Guid sessionId) =>
        sessionTeacherUserIds.TryGetValue(sessionId, out var userIds)
            ? userIds
            : throw new InvalidOperationException($"Session {sessionId} has no teacher assignment.");

    public bool IsSessionOwner(Guid sessionId, string teacherUserId) =>
        TeacherUserIdsFor(sessionId)[0] == teacherUserId;

    public static async Task<TestEnvironment> CreateAsync(Uri gateway, string runId, int sessionCount,
        string[] tokens, MetricsCollector metrics, CancellationToken cancellationToken = default,
        TimeSpan? requestTimeout = null)
    {
        if (tokens.Length != 30 || tokens.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Exactly thirty teacher JWTs must be supplied through environment variables.", nameof(tokens));
        var clients = tokens.Select(token => new GatewayClient(gateway, metrics, requestTimeout, jwt: token)).ToArray();
        return await CreateAsync(gateway, runId, sessionCount, clients, metrics, cancellationToken);
    }

    /// <summary>Uses authenticated teachers; the caller can retain their login across warm-up and measurement.</summary>
    public static async Task<TestEnvironment> CreateAsync(Uri gateway, string runId, int sessionCount,
        GatewayClient[] authenticatedTeachers, MetricsCollector metrics, CancellationToken cancellationToken = default,
        bool disposeTeachers = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (sessionCount is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(sessionCount), "Thirty teachers support at most ten sessions with three teachers per session.");
        if (authenticatedTeachers.Length != 30)
            throw new ArgumentException("Exactly thirty teacher clients are required.", nameof(authenticatedTeachers));

        var environment = new TestEnvironment(metrics, disposeTeachers);
        environment.teachers.AddRange(authenticatedTeachers);
        try
        {
            // /teachers/me traverses both gateway and Eir authentication (signature, issuer, audience,
            // expiry, Teacher role and Bifrost scope). We never trust a locally decoded JWT payload.
            foreach (var teacher in environment.teachers)
            {
                await teacher.AuthenticateTeacherAsync(cancellationToken);
            }
            if (environment.teachers.Select(teacher => teacher.UserId).Distinct(StringComparer.Ordinal).Count() != 30)
                throw new InvalidOperationException("The thirty teacher JWTs must represent thirty separate authenticated users.");

            for (var index = 0; index < sessionCount; index++)
            {
                var groupStart = (index % 10) * 3;
                var owner = environment.teachers[groupStart];
                var session = await owner.CreateSessionAsync($"Performance {runId} / lesson {index + 1}", cancellationToken);
                environment.sessions.Add(session); // Track immediately so a later join failure is cleaned up.
                environment.sessionOwners[session.Id] = owner;
                var group = environment.teachers.Skip(groupStart).Take(3).ToArray();
                environment.sessionTeacherUserIds[session.Id] = group.Select(teacher => teacher.UserId!).ToArray();
                foreach (var colleague in group.Skip(1))
                {
                    await colleague.JoinTeacherAsync(session.TeacherCode, cancellationToken);
                    await colleague.GetTeacherSessionAsync(session.Id, cancellationToken);
                }
                // A queue belongs to its session. Reading it verifies teacher membership and readiness.
                await owner.GetTeacherQueueAsync(session.Id, cancellationToken);
            }
            return environment;
        }
        catch
        {
            // Setup cancellation must not prevent cleanup of sessions already created.
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await environment.CleanupAsync(cleanupTimeout.Token); }
            catch (Exception exception) { metrics.RecordFailure("setup_cleanup", exception.GetType().Name); }
            if (environment.disposeTeachers)
                foreach (var teacher in environment.teachers) teacher.Dispose();
            throw;
        }
    }

    public async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        if (teachers.Count == 0) return;
        List<Exception> errors = [];
        foreach (var session in sessions.Where(session => !deleted.Contains(session.Id)))
        {
            var owner = sessionOwners[session.Id];
            try
            {
                try
                {
                    await owner.SendAsync(HttpMethod.Delete, $"/api/teachers/sessions/{session.Id}", cancellationToken: cancellationToken);
                }
                catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
                {
                    // A previous cleanup may have deleted the session before verification failed.
                    // Access/list checks below still have to prove it is gone for every teacher.
                }
                // Every teacher must lose access once deletion completes. Checking the detail and the
                // list prevents declaring success solely on DELETE's HTTP 204 response.
                foreach (var teacher in teachers)
                    await teacher.SendAsync(HttpMethod.Get, $"/api/teachers/sessions/{session.Id}",
                        cancellationToken: cancellationToken, expectedStatus: HttpStatusCode.NotFound);
                foreach (var teacher in teachers)
                {
                    var listed = await teacher.SendAsync<System.Text.Json.JsonElement>(HttpMethod.Get,
                        "/api/teachers/sessions", cancellationToken: cancellationToken);
                    if (listed.ValueKind != System.Text.Json.JsonValueKind.Array ||
                        listed.EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == session.Id))
                        throw new InvalidOperationException($"Deleted test session {session.Id} remains in the teacher list.");
                }
                deleted.Add(session.Id);
            }
            catch (Exception exception)
            {
                metrics.RecordFailure("cleanup_session", $"{session.Id}:{exception.GetType().Name}", owner.UserId);
                errors.Add(exception);
            }
        }
        if (errors.Count != 0)
            throw new AggregateException("Some test sessions could not be deleted and verified. Use the run manifest to retry cleanup.", errors);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await CleanupAsync(cleanupTimeout.Token);
        }
        finally
        {
            if (disposeTeachers)
                foreach (var teacher in teachers) teacher.Dispose();
        }
    }
}
