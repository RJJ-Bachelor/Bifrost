using System.Net;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Backend.Performance.Tests.Reporting;
using EirService.Api.Endpoints.Student.HelpRequests.AddStudentMessage;
using EirService.Api.Endpoints.Student.HelpRequests.CancelHelpRequest;
using EirService.Api.Endpoints.Student.HelpRequests.CreateHelpRequest;
using EirService.Api.Endpoints.Student.HelpRequests.RespondToFaq;
using EirService.Api.Endpoints.Student.Participants.GetStudentIdentity;
using EirService.Api.Endpoints.Student.Participants.JoinSession;
using EirService.Api.Endpoints.Teacher.HelpRequests.AnswerHelpRequest;
using EirService.Api.Endpoints.Teacher.HelpRequests.ChangeHelpRequestStatus;
using EirService.Api.Endpoints.Teacher.Participants.GetTeacherIdentity;
using EirService.Api.Endpoints.Teacher.Participants.JoinSession;
using EirService.Api.Endpoints.Teacher.Sessions.CreateSession;
using EirService.Contracts.Sessions.Common;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Features.Student.GetStudentQueue;
using EirService.HelpRequests.Application.Features.Teacher.ChangeHelpRequestStatus;
using EirService.HelpRequests.Application.Features.Teacher.GetQueue;
using EirService.HelpRequests.Domain.Entities;
using EirService.Sessions.Application.Features.Teacher.Common;
using Shared.Api.Authentication;

namespace Backend.Performance.Tests.Infrastructure;

/// <summary>A real HTTP client to Heimdall; it never creates identities, signs tokens or bypasses policies.</summary>
public sealed class GatewayClient : IDisposable
{
    private static readonly HttpMessageInvoker SharedTransport = CreateTransport(1024);
    // Long polling may keep one HTTP request open per student. Keep its capacity independent
    // of business HTTP calls, while avoiding an idle negotiation pool for every identity.
    private static readonly HttpMessageInvoker SharedSignalRTransport = CreateTransport(2048);

    private static HttpMessageInvoker CreateTransport(int maximumConnections) => new(new SocketsHttpHandler
    {
        // Identity lives in each client's cookie handler, never in this shared connection pool.
        UseCookies = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        MaxConnectionsPerServer = maximumConnections
    });
    private readonly MetricsCollector metrics;

    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public GatewayClient(Uri gateway, MetricsCollector metrics, TimeSpan? requestTimeout = null,
        CookieContainer? cookies = null, string? jwt = null)
    {
        if (!gateway.IsAbsoluteUri || gateway.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(gateway.UserInfo) || !string.IsNullOrEmpty(gateway.Query) ||
            !string.IsNullOrEmpty(gateway.Fragment))
            throw new ArgumentException("Gateway must be an absolute HTTP(S) URL without credentials, query or fragment.", nameof(gateway));

        this.metrics = metrics;
        Cookies = cookies ?? new CookieContainer();
        Http = new HttpClient(new IdentityCookieHandler(Cookies, SharedTransport))
        {
            BaseAddress = gateway,
            Timeout = requestTimeout ?? TimeSpan.FromSeconds(30)
        };
        if (!string.IsNullOrWhiteSpace(jwt))
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
    }

    public HttpClient Http { get; }
    public CookieContainer Cookies { get; }
    public string? UserId { get; private set; }

    internal HttpMessageHandler CreateSignalRHttpHandler(HttpMessageHandler unusedHandler)
    {
        unusedHandler.Dispose();
        return new IdentityCookieHandler(Cookies, SharedSignalRTransport);
    }

    public async Task<StudentIdentityResponse> AuthenticateStudentAsync(CancellationToken cancellationToken = default)
    {
        var identity = await RequiredAsync<StudentIdentityResponse>(HttpMethod.Get, "/api/students/me", cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(identity.UserId) || !identity.Roles.Contains("Student", StringComparer.Ordinal) ||
            Cookies.GetCookies(new Uri(Http.BaseAddress!, "/api/students/me"))[StudentCookieHandler.CookieName] is null)
            throw new InvalidOperationException("The gateway did not return an authenticated student and its protected cookie.");
        UserId = identity.UserId;
        return identity;
    }

    public async Task<TeacherIdentityResponse> AuthenticateTeacherAsync(CancellationToken cancellationToken = default)
    {
        var identity = await RequiredAsync<TeacherIdentityResponse>(HttpMethod.Get, "/api/teachers/me", cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(identity.UserId) || !identity.Roles.Contains("Teacher", StringComparer.Ordinal))
            throw new InvalidOperationException("The gateway did not validate a teacher identity with the required role and scope.");
        UserId = identity.UserId;
        return identity;
    }

    public Task<SessionResult> CreateSessionAsync(string title, CancellationToken cancellationToken = default) =>
        RequiredAsync<SessionResult>(HttpMethod.Post, "/api/teachers/sessions", new CreateSessionRequest { Title = title }, cancellationToken);

    public Task<SessionSummary> JoinStudentAsync(string code, CancellationToken cancellationToken = default) =>
        RequiredAsync<SessionSummary>(HttpMethod.Post, "/api/students/sessions/join", new JoinSessionRequest { Code = code }, cancellationToken);

    public Task<SessionResult> JoinTeacherAsync(string code, CancellationToken cancellationToken = default) =>
        RequiredAsync<SessionResult>(HttpMethod.Post, "/api/teachers/sessions/join", new JoinTeacherSessionRequest { Code = code }, cancellationToken);

    public Task<SessionResult> GetTeacherSessionAsync(Guid id, CancellationToken cancellationToken = default) =>
        RequiredAsync<SessionResult>(HttpMethod.Get, $"/api/teachers/sessions/{id}", cancellationToken: cancellationToken);

    public Task<HelpRequestResult> CreateHelpRequestAsync(Guid sessionId, string message, HelpType type,
        CancellationToken cancellationToken = default, Action? beforeSend = null) =>
        RequiredAsync<HelpRequestResult>(HttpMethod.Post, $"/api/students/sessions/{sessionId}/requests",
            new CreateHelpRequestRequest { Message = message, HelpType = type }, cancellationToken, beforeSend);

    public Task<HelpRequestResult?> TakeNextAsync(Guid sessionId, CancellationToken cancellationToken = default, Action? beforeSend = null) =>
        SendAsync<HelpRequestResult>(HttpMethod.Post, $"/api/teachers/sessions/{sessionId}/queue/take-next",
            cancellationToken: cancellationToken, allowNoContent: true, beforeSend: beforeSend);

    public Task<HelpRequestResult> AnswerAsync(Guid id, string text, CancellationToken cancellationToken = default, Action? beforeSend = null) =>
        RequiredAsync<HelpRequestResult>(HttpMethod.Post, $"/api/teachers/requests/{id}/messages",
            new AnswerHelpRequestRequest { Text = text }, cancellationToken, beforeSend);

    public Task<HelpRequestResult> CancelAsync(Guid id, CancellationToken cancellationToken = default, Action? beforeSend = null) =>
        RequiredAsync<HelpRequestResult>(HttpMethod.Patch, $"/api/students/requests/{id}/status",
            new CancelHelpRequestRequest { Action = "Cancel" }, cancellationToken, beforeSend);

    public Task<HelpRequestResult> RespondToFaqAsync(Guid id, bool accept, CancellationToken cancellationToken = default, Action? beforeSend = null) =>
        RequiredAsync<HelpRequestResult>(HttpMethod.Patch, $"/api/students/requests/{id}/faq",
            new RespondToFaqRequest { Accept = accept }, cancellationToken, beforeSend);

    public Task<HelpRequestResult> ChangeStatusAsync(Guid id, HelpRequestAction action,
        CancellationToken cancellationToken = default, Action? beforeSend = null) =>
        RequiredAsync<HelpRequestResult>(HttpMethod.Patch, $"/api/teachers/requests/{id}/status",
            new ChangeHelpRequestStatusRequest { Action = action }, cancellationToken, beforeSend);

    public Task<HelpRequestResult> GetStudentRequestAsync(Guid id, CancellationToken cancellationToken = default) =>
        RequiredAsync<HelpRequestResult>(HttpMethod.Get, $"/api/students/requests/{id}", cancellationToken: cancellationToken);

    public Task<HelpRequestResult> AddStudentMessageAsync(Guid id, string text,
        CancellationToken cancellationToken = default, Action? beforeSend = null) =>
        RequiredAsync<HelpRequestResult>(HttpMethod.Post, $"/api/students/requests/{id}/messages",
            new AddStudentMessageRequest { Text = text }, cancellationToken, beforeSend);

    public Task<StudentQueueResult> GetStudentQueueAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        RequiredAsync<StudentQueueResult>(HttpMethod.Get, $"/api/students/sessions/{sessionId}/queue", cancellationToken: cancellationToken);

    public Task<TeacherQueueResult> GetTeacherQueueAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        RequiredAsync<TeacherQueueResult>(HttpMethod.Get, $"/api/teachers/sessions/{sessionId}/queue", cancellationToken: cancellationToken);

    /// <summary>HTTP timing ends when the response body is received; DTO decoding is outside that timing.</summary>
    public async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null,
        CancellationToken cancellationToken = default, bool allowNoContent = false, Action? beforeSend = null)
    {
        using var request = CreateRequest(method, path, body);
        var operation = OperationName(method, path);
        var started = metrics.Capture();
        int? status = null;
        var success = false;
        string? error = null;
        long? responseReceived = null;
        try
        {
            beforeSend?.Invoke();
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            responseReceived = Stopwatch.GetTimestamp();
            status = (int)response.StatusCode;
            response.EnsureSuccessStatusCode();
            if (response.StatusCode == HttpStatusCode.NoContent && allowNoContent)
            {
                success = true;
                return default;
            }
            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            if (result is null) throw new JsonException("The gateway returned an empty JSON result.");
            success = true;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error = "CallerCancellation";
            throw;
        }
        catch (Exception exception)
        {
            // Never persist response bodies, Authorization headers, cookies or token-bearing URLs.
            error = exception.GetType().Name;
            throw;
        }
        finally
        {
            metrics.RecordOperation(error == "CallerCancellation" ? "cancelled" : "http",
                operation, started, success, status, UserId, error: error, completedTimestamp: responseReceived);
        }
    }

    /// <summary>For lifecycle operations without a JSON response. Expected 404 verification is successful.</summary>
    public async Task<HttpStatusCode> SendAsync(HttpMethod method, string path, object? body = null,
        CancellationToken cancellationToken = default, HttpStatusCode? expectedStatus = null)
    {
        using var request = CreateRequest(method, path, body);
        var started = metrics.Capture();
        int? status = null;
        var success = false;
        string? error = null;
        try
        {
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            status = (int)response.StatusCode;
            if (expectedStatus.HasValue && response.StatusCode != expectedStatus.Value)
                throw new HttpRequestException($"Gateway returned HTTP {status}; expected {(int)expectedStatus.Value}.", null, response.StatusCode);
            if (!expectedStatus.HasValue) response.EnsureSuccessStatusCode();
            success = true;
            return response.StatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error = "CallerCancellation";
            throw;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name;
            throw;
        }
        finally
        {
            metrics.RecordOperation(error == "CallerCancellation" ? "cancelled" : "http",
                OperationName(method, path), started, success, status, UserId, error: error);
        }
    }

    private async Task<T> RequiredAsync<T>(HttpMethod method, string path, object? body = null,
        CancellationToken cancellationToken = default, Action? beforeSend = null) =>
        (await SendAsync<T>(method, path, body, cancellationToken, beforeSend: beforeSend))!;

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body)
    {
        // All requests stay on the configured gateway and use the gateway's public API routes.
        if (!path.StartsWith("/api/", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("Only gateway /api/ routes are supported.", nameof(path));
        return new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: JsonOptions)
        };
    }

    private static string OperationName(HttpMethod method, string path) =>
        $"{method} {Regex.Replace(path.Split('?')[0], @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "{id}")}";

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public void Dispose() => Http.Dispose();
}
