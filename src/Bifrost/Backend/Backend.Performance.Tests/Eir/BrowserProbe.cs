using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Performance.Tests.Configuration;
using Backend.Performance.Tests.Infrastructure;
using Backend.Performance.Tests.Reporting;
using Microsoft.Playwright;
using Shared.Api.Authentication;
using Shared.Application.Messaging.Contracts;

namespace Backend.Performance.Tests.Eir;

/// <summary>
/// A few additional, real frontend students. Measures submit-to-painted-queue and matching
/// notification-to-painted-answer in the browser's monotonic performance.now clock.
/// </summary>
public sealed class BrowserProbe(
    PerformanceOptions options, MetricsCollector metrics, TestEnvironment environment,
    NotificationTracker? notifications = null, string? runDirectory = null) : IAsyncDisposable
{
    private readonly List<Observer> observers = [];
    private readonly CancellationTokenSource stop = new();
    private IPlaywright? playwright;
    private IBrowser? browser;
    private bool started;
    private bool disposed;

    public IReadOnlyList<string> UserIds => observers.Select(observer => observer.Client.UserId!).ToArray();

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (started) throw new InvalidOperationException("Browser probes have already been started.");
        started = true;
        if (options.BrowserClients == 0) return;
        var frontend = options.FrontendUrl ?? throw new ArgumentException("FrontendUrl is required for browser probes.");
        if (!frontend.IsAbsoluteUri || frontend.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(frontend.UserInfo) || !string.IsNullOrEmpty(frontend.Query) ||
            !string.IsNullOrEmpty(frontend.Fragment) ||
            !frontend.IdnHost.Equals(options.GatewayUrl.IdnHost, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("FrontendUrl must use the gateway's hostname, with no credentials, query or fragment. Its /api routes must proxy to that gateway.");
        try
        {
            playwright = await Playwright.CreateAsync().WaitAsync(cancellationToken);
            browser = await playwright.Chromium.LaunchAsync(new() { Headless = true }).WaitAsync(cancellationToken);
            for (var index = 0; index < options.BrowserClients; index++)
            {
                var client = new GatewayClient(options.GatewayUrl, metrics, TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
                var observer = new Observer(index, client);
                observers.Add(observer); // Register immediately so partial initialization is cleaned up.
                await client.AuthenticateStudentAsync(cancellationToken);
                var lesson = environment.Sessions[options.LessonFor(index)];
                observer.SessionId = lesson.Id;
                await client.JoinStudentAsync(lesson.Code, cancellationToken);
                observer.Context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } }).WaitAsync(cancellationToken);
                observer.Context.SetDefaultTimeout((float)(options.RequestTimeoutSeconds * 1000));
                var identityCookie = client.Cookies.GetCookies(new Uri(options.GatewayUrl, "/api/students/me"))[StudentCookieHandler.CookieName]
                    ?? throw new InvalidOperationException("Gateway did not issue the browser observer's student cookie.");
                if (identityCookie.Secure && frontend.Scheme != "https")
                    throw new InvalidOperationException("A Secure student cookie requires an HTTPS frontend; the browser probe will not weaken cookie security.");
                await observer.Context.AddCookiesAsync([ToBrowserCookie(identityCookie)]).WaitAsync(cancellationToken);
                await observer.Context.AddInitScriptAsync(ObservationScript).WaitAsync(cancellationToken);
                observer.Page = await observer.Context.NewPageAsync().WaitAsync(cancellationToken);
                ObserveNetwork(observer);
                await observer.Page.GotoAsync(new Uri(frontend, $"/sessions/{lesson.Id}").AbsoluteUri,
                    new() { WaitUntil = WaitUntilState.DOMContentLoaded }).WaitAsync(cancellationToken);
                var browserIdentity = await observer.Page.EvaluateAsync<JsonElement>("""
                    async () => {
                        const response = await fetch('/api/students/me', { credentials: 'include' });
                        if (!response.ok) throw new Error('Browser identity endpoint rejected authentication.');
                        return await response.json();
                    }
                    """).WaitAsync(cancellationToken);
                if (!browserIdentity.TryGetProperty("userId", out var userId) || userId.GetString() != client.UserId)
                    throw new InvalidOperationException("Frontend proxy did not preserve the gateway-issued student identity.");
                await observer.Page.Locator("bifrost-message-form textarea").WaitForAsync(new() { State = WaitForSelectorState.Visible }).WaitAsync(cancellationToken);
                await observer.Connected.Task.WaitAsync(TimeSpan.FromSeconds(options.RequestTimeoutSeconds), cancellationToken);
            }
            // Authentication and initial render must all succeed before background actions start.
            foreach (var observer in observers) observer.Worker = RunObserverAsync(observer, stop.Token);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    private static Microsoft.Playwright.Cookie ToBrowserCookie(System.Net.Cookie cookie) => new()
    {
        Name = cookie.Name,
        Value = cookie.Value,
        Domain = cookie.Domain,
        Path = cookie.Path,
        HttpOnly = cookie.HttpOnly,
        Secure = cookie.Secure,
        // The actual bootstrap endpoint issues SameSite=Lax. CookieContainer does not retain it.
        SameSite = SameSiteAttribute.Lax,
        Expires = cookie.Expires == DateTime.MinValue ? null : (float)new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds()
    };

    private void ObserveNetwork(Observer observer)
    {
        var page = observer.Page!;
        page.Request += (_, request) =>
        {
            if (IsApi(request.Url)) observer.Requests[request] = new(metrics.Capture(), OperationName(request));
        };
        page.Response += (_, response) =>
        {
            if (observer.Requests.TryGetValue(response.Request, out var request)) request.Status = response.Status;
        };
        page.RequestFinished += (_, request) =>
        {
            if (!observer.Requests.TryRemove(request, out var observation)) return;
            var duration = request.Timing.ResponseEnd;
            var success = observation.Status is >= 200 and < 400;
            metrics.RecordOperation("http", observation.Operation, observation.Started, success,
                observation.Status, observer.Actor,
                error: success ? null : "Browser observed an unsuccessful HTTP status.",
                completedTimestamp: duration >= 0 ? EndTimestamp(observation.Started, duration) : null);
        };
        page.RequestFailed += (_, request) =>
        {
            if (!observer.Requests.TryRemove(request, out var observation)) return;
            // Angular cancels obsolete resource loads during navigation/reload. An aborted
            // request is preserved as cancelled raw data and never relabelled as successful HTTP.
            var cancelled = request.Failure?.Contains("ERR_ABORTED", StringComparison.OrdinalIgnoreCase) == true;
            metrics.RecordOperation(cancelled ? "cancelled" : "http", observation.Operation, observation.Started, false,
                observation.Status, observer.Actor,
                error: cancelled ? "Browser request aborted (ERR_ABORTED)." : "Browser HTTP transport failed.");
        };
        page.WebSocket += (_, socket) =>
        {
            if (!new Uri(socket.Url).AbsolutePath.Equals("/api/students/notifications", StringComparison.Ordinal)) return;
            var acknowledged = false;
            socket.FrameReceived += (_, frame) =>
            {
                if (frame.Text is not { } payload) return;
                foreach (var text in payload.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        using var json = JsonDocument.Parse(text);
                        var root = json.RootElement;
                        if (!acknowledged && !root.TryGetProperty("type", out var frameType) && !root.TryGetProperty("error", out var frameError))
                        {
                            acknowledged = true; // A real SignalR handshake acknowledgement, not a socket attempt.
                            if (Interlocked.Exchange(ref observer.IsConnected, 1) == 0)
                            {
                                if (observer.HasConnected) metrics.ConnectionReconnected(observer.Actor);
                                else metrics.ConnectionOpened(observer.Actor);
                            }
                            observer.HasConnected = true;
                            observer.Connected.TrySetResult();
                        }
                        if (!root.TryGetProperty("target", out var target) || target.GetString() != "NotificationReceived" ||
                            !root.TryGetProperty("arguments", out var arguments) || arguments.GetArrayLength() == 0) continue;
                        var message = arguments[0].Deserialize<NotificationDeliveryRequestedMessage>(GatewayClient.JsonOptions);
                        if (message is null) continue;
                        notifications?.Observe(message, observer.Client.UserId!);
                        metrics.NotificationReceived(observer.Actor, message.HelpRequestId?.ToString(), message.EventType.ToString());
                        if (message.UserId == observer.Client.UserId && message.HelpRequestId is { } id &&
                            (message.EventType.ToString() is "HelpRequestAnswered" or "FaqAnswered" || message.Change == "FaqAnswered"))
                            observer.FeedbackStamps.TryAdd(id, metrics.Capture());
                    }
                    catch (JsonException) { metrics.RecordFailure("browser.signalr_frame", "Unexpected SignalR JSON frame.", observer.Actor); }
                }
            };
            socket.Close += (_, _) =>
            {
                if (Interlocked.Exchange(ref observer.IsConnected, 0) == 1)
                    metrics.ConnectionClosed(observer.Actor, stop.IsCancellationRequested ? "Scheduled browser closure." : "Browser WebSocket closed.");
            };
            socket.SocketError += (_, _) => metrics.RecordFailure("browser.websocket", "Browser WebSocket error.", observer.Actor);
        };
    }

    private async Task RunObserverAsync(Observer observer, CancellationToken cancellationToken)
    {
        var page = observer.Page!;
        while (!cancellationToken.IsCancellationRequested)
        {
            OperationStamp? pending = null;
            var operation = "browser.submit-to-visible-queue";
            Guid? requestId = null;
            try
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Digitalt", Exact = true }).ClickAsync().WaitAsync(cancellationToken);
                await page.Locator("bifrost-message-form textarea").FillAsync($"Performance browser {observer.Actor}: {Guid.NewGuid():N}").WaitAsync(cancellationToken);
                pending = metrics.Capture();
                var response = await page.RunAndWaitForResponseAsync(
                    () => page.Locator("bifrost-message-form button[type=submit]").ClickAsync(),
                    response => response.Request.Method == "POST" &&
                        new Uri(response.Url).AbsolutePath == $"/api/students/sessions/{observer.SessionId}/requests").WaitAsync(cancellationToken);
                if (!response.Ok) throw new InvalidOperationException($"Browser create returned HTTP {response.Status}.");
                var created = await response.JsonAsync().WaitAsync(cancellationToken);
                if (created is not { } body || !body.TryGetProperty("id", out var idProperty) || !idProperty.TryGetGuid(out var id))
                    throw new InvalidOperationException("Browser create did not return a request ID.");
                requestId = id;
                await page.EvaluateAsync("id => window.__bifrostPerf.bind(id)", id.ToString()).WaitAsync(cancellationToken);
                await page.WaitForFunctionAsync("() => window.__bifrostPerf.queuePaintMs !== null").WaitAsync(cancellationToken);
                var queueMilliseconds = await page.EvaluateAsync<double>("() => window.__bifrostPerf.queuePaintMs").WaitAsync(cancellationToken);
                RecordBrowser(operation, pending.Value, queueMilliseconds, observer.Actor, id);
                pending = null;

                operation = "browser.notification-to-visible-answer";
                pending = metrics.Capture(); // A missing/late teacher answer is an explicit failed browser probe.
                await page.WaitForFunctionAsync("() => window.__bifrostPerf.feedbackPaintMs !== null", null,
                    new() { Timeout = (float)(Math.Max(options.NotificationTimeoutSeconds, options.RequestTimeoutSeconds) * 1000) }).WaitAsync(cancellationToken);
                var feedbackMilliseconds = await page.EvaluateAsync<double>("() => window.__bifrostPerf.feedbackPaintMs").WaitAsync(cancellationToken);
                if (!observer.FeedbackStamps.TryRemove(id, out var received))
                    throw new InvalidOperationException("Visible feedback lacked a matching observed SignalR message.");
                pending = received;
                RecordBrowser(operation, received, feedbackMilliseconds, observer.Actor, id);
                pending = null;
                var faq = await page.Locator("bifrost-queue-awaiting-confirmation").IsVisibleAsync().WaitAsync(cancellationToken);
                if (faq)
                    await page.GetByRole(AriaRole.Button, new() { Name = "Svar nok", Exact = true }).ClickAsync().WaitAsync(cancellationToken);
                else
                    await page.Locator("bifrost-chat").GetByRole(AriaRole.Button, new() { Name = "Luk", Exact = true }).ClickAsync().WaitAsync(cancellationToken);
                await page.Locator("bifrost-message-form textarea").WaitForAsync(new() { State = WaitForSelectorState.Visible }).WaitAsync(cancellationToken);
                await Task.Delay(options.Pause(teacher: false), cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                if (pending is { } stamp)
                    metrics.RecordOperation("cancelled", operation, stamp, false, actor: observer.Actor,
                        correlationId: requestId?.ToString(), error: "Scheduled browser observer stop before visible feedback completed.");
                return;
            }
            catch (Exception exception)
            {
                // A selector mismatch, absent notification, missing browser or bad proxy must be visible
                // as a failed probe. Do not silently fall back to HTTP or fabricate a render duration.
                var failedAt = Stopwatch.GetTimestamp();
                var diagnostic = await CaptureDiagnosticsAsync(observer, operation, requestId, exception.GetType().Name);
                if (pending is { } stamp)
                    metrics.RecordOperation("browser", operation, stamp, false, actor: observer.Actor,
                        correlationId: requestId?.ToString(), error: diagnostic, completedTimestamp: failedAt);
                else metrics.RecordFailure(operation, diagnostic, observer.Actor);
                return; // Retain the failed page/session for normal run-owned cleanup.
            }
        }
    }

    private async Task<string> CaptureDiagnosticsAsync(Observer observer, string operation, Guid? requestId, string failureType)
    {
        var directory = Path.Combine(runDirectory ?? options.OutputDirectory, "browser-diagnostics");
        var filename = $"{observer.Actor}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        JsonElement? state = null;
        string? diagnosticError = null;
        var screenshotSaved = false;
        try
        {
            Directory.CreateDirectory(directory);
            if (observer.Page is { IsClosed: false } page)
            {
                state = await page.EvaluateAsync<JsonElement>(DiagnosticScript).WaitAsync(TimeSpan.FromSeconds(5));
                await page.ScreenshotAsync(new() { Path = Path.Combine(directory, filename + ".png"), FullPage = true })
                    .WaitAsync(TimeSpan.FromSeconds(5));
                screenshotSaved = true;
            }
        }
        catch (Exception exception) { diagnosticError = exception.GetType().Name; }

        // Persist only allowlisted state: path, random run-owned IDs, event types, counts and
        // element geometry. Never read document.cookie, storage, headers, message text or URLs.
        var evidence = new
        {
            FailureType = failureType, Operation = operation, Actor = observer.Actor,
            SessionId = observer.SessionId, RequestId = requestId, BrowserState = state,
            ObservedFeedbackIds = observer.FeedbackStamps.Keys.Order().ToArray(),
            PendingHttp = observer.Requests.Values.Select(value => new { value.Operation, value.Status }).ToArray(),
            Screenshot = screenshotSaved ? filename + ".png" : null, DiagnosticError = diagnosticError
        };
        var json = JsonSerializer.Serialize(evidence);
        try { await File.WriteAllTextAsync(Path.Combine(directory, filename + ".json"), json); }
        catch (Exception exception) { return failureType + "; diagnostic write failed: " + exception.GetType().Name + "; " + json; }
        return failureType + "; diagnostics=browser-diagnostics/" + filename + ".json; " + json;
    }

    private void RecordBrowser(string operation, OperationStamp started, double durationMs, string actor, Guid requestId)
    {
        if (!double.IsFinite(durationMs) || durationMs < 0) throw new InvalidOperationException("Invalid browser monotonic duration.");
        metrics.RecordOperation("browser", operation, started, true, actor: actor,
            correlationId: requestId.ToString(), completedTimestamp: EndTimestamp(started, durationMs));
    }

    private static long EndTimestamp(OperationStamp stamp, double milliseconds) =>
        checked(stamp.Timestamp + (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000));

    private static bool IsApi(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal);

    private static string OperationName(IRequest request) => "browser: " + request.Method + " " +
        Regex.Replace(new Uri(request.Url).AbsolutePath,
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "{id}");

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        foreach (var observer in observers) metrics.ConnectionCloseScheduled(observer.Actor);
        await stop.CancelAsync();
        try
        {
            if (browser is not null) await browser.CloseAsync(); // Unblocks outstanding Playwright waits.
        }
        catch (PlaywrightException exception)
        {
            metrics.RecordFailure("browser.close", exception.GetType().Name);
        }
        foreach (var observer in observers)
        {
            if (observer.Worker is not null) await observer.Worker;
            if (Interlocked.Exchange(ref observer.IsConnected, 0) == 1)
                metrics.ConnectionClosed(observer.Actor, "Scheduled browser closure.");
            observer.Client.Dispose();
        }
        playwright?.Dispose();
    }

    private sealed class Observer(int index, GatewayClient client)
    {
        public string Actor { get; } = $"browser-{index + 1}";
        public GatewayClient Client { get; } = client;
        public Guid SessionId { get; set; }
        public IBrowserContext? Context { get; set; }
        public IPage? Page { get; set; }
        public Task? Worker { get; set; }
        public int IsConnected;
        public bool HasConnected { get; set; }
        public TaskCompletionSource Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentDictionary<IRequest, HttpObservation> Requests { get; } = new();
        public ConcurrentDictionary<Guid, OperationStamp> FeedbackStamps { get; } = new();
    }

    private sealed class HttpObservation(OperationStamp started, string operation)
    {
        public OperationStamp Started { get; } = started;
        public string Operation { get; } = operation;
        public int? Status { get; set; }
    }

    // Observational instrumentation only: native WebSocket transport, UI handlers and API calls
    // still execute unchanged. A double RAF after a visible DOM change allows a paint opportunity.
    private const string ObservationScript = """
        (() => {
            const state = window.__bifrostPerf = {
                submittedAt: null, requestId: null, queuePaintMs: null, feedbackPaintMs: null,
                notifications: new Map(), queuePending: false, feedbackPending: false,
                observedSockets: 0, receivedFrames: 0, textFrames: 0, observedNotifications: [],
                bind(id) { this.requestId = id; check(); }
            };
            const visible = selector => [...document.querySelectorAll(selector)].find(element => {
                const bounds = element.getBoundingClientRect();
                const style = getComputedStyle(element);
                return bounds.width > 0 && bounds.height > 0 && style.visibility !== 'hidden' &&
                    style.display !== 'none' && bounds.bottom > 0 && bounds.top < innerHeight;
            });
            const answerVisible = () =>
                !!visible('.chat__bubble--teacher .chat__text') ||
                !!visible('bifrost-queue-awaiting-confirmation .faq-card__answer');
            const afterPaint = callback => requestAnimationFrame(() => requestAnimationFrame(callback));
            function check() {
                if (state.submittedAt === null) return;
                if (state.queuePaintMs === null && !state.queuePending &&
                    location.pathname.endsWith('/queue') && visible('.queue-card-wrapper')) {
                    state.queuePending = true;
                    afterPaint(() => {
                        if (visible('.queue-card-wrapper')) state.queuePaintMs = performance.now() - state.submittedAt;
                        state.queuePending = false;
                    });
                }
                const notifiedAt = state.notifications.get(state.requestId);
                if (state.feedbackPaintMs === null && !state.feedbackPending &&
                    notifiedAt !== undefined && notifiedAt >= state.submittedAt && answerVisible()) {
                    state.feedbackPending = true;
                    afterPaint(() => {
                        if (answerVisible()) state.feedbackPaintMs = performance.now() - notifiedAt;
                        state.feedbackPending = false;
                    });
                }
            }
            document.addEventListener('submit', event => {
                if (!event.target.closest('bifrost-message-form')) return;
                state.submittedAt = performance.now();
                state.requestId = null;
                state.queuePaintMs = state.feedbackPaintMs = null;
                state.queuePending = state.feedbackPending = false;
                state.notifications.clear();
            }, true);
            const NativeWebSocket = window.WebSocket;
            window.WebSocket = class extends NativeWebSocket {
                constructor(...args) {
                    super(...args);
                    state.observedSockets++;
                    this.addEventListener('message', event => {
                        state.receivedFrames++;
                        if (typeof event.data !== 'string') return;
                        state.textFrames++;
                        for (const frame of event.data.split('\x1e')) {
                            if (!frame) continue;
                            try {
                                const invocation = JSON.parse(frame);
                                if (invocation.target !== 'NotificationReceived') continue;
                                const message = invocation.arguments?.[0];
                                if (message) {
                                    state.observedNotifications.push({
                                        eventType: message.eventType, change: message.change,
                                        helpRequestId: message.helpRequestId, sessionId: message.sessionId,
                                        receivedAt: performance.now()
                                    });
                                    if (state.observedNotifications.length > 20) state.observedNotifications.shift();
                                }
                                if (!message?.helpRequestId) continue;
                                if (message.eventType === 'HelpRequestAnswered' || message.eventType === 'FaqAnswered' ||
                                    message.change === 'FaqAnswered') {
                                    if (!state.notifications.has(message.helpRequestId))
                                        state.notifications.set(message.helpRequestId, performance.now());
                                    check();
                                }
                            } catch { /* SignalR's own protocol handler still validates every frame. */ }
                        }
                    });
                }
            };
            new MutationObserver(check).observe(document, { childList: true, subtree: true, attributes: true, characterData: true });
        })();
        """;

    private const string DiagnosticScript = """
        () => {
            const state = window.__bifrostPerf;
            const elements = selector => [...document.querySelectorAll(selector)].map(element => {
                const bounds = element.getBoundingClientRect();
                const style = getComputedStyle(element);
                return {
                    x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height,
                    display: style.display, visibility: style.visibility,
                    nonEmpty: !!element.textContent?.trim()
                };
            });
            return {
                path: location.pathname, now: performance.now(), visibility: document.visibilityState,
                viewport: { width: innerWidth, height: innerHeight, scrollX, scrollY },
                probe: state ? {
                    submittedAt: state.submittedAt, requestId: state.requestId,
                    queuePaintMs: state.queuePaintMs, feedbackPaintMs: state.feedbackPaintMs,
                    queuePending: state.queuePending, feedbackPending: state.feedbackPending,
                    observedSockets: state.observedSockets, receivedFrames: state.receivedFrames,
                    textFrames: state.textFrames, observedNotifications: state.observedNotifications,
                    matchingNotifications: [...state.notifications].map(([requestId, receivedAt]) => ({ requestId, receivedAt }))
                } : null,
                queueCards: elements('.queue-card-wrapper'),
                chat: elements('.chat'), teacherText: elements('.chat__bubble--teacher .chat__text'),
                faqAnswer: elements('bifrost-queue-awaiting-confirmation .faq-card__answer'),
                questionForm: elements('bifrost-message-form'), openRequest: elements('.session-open-request'),
                errors: elements('.form-error, .message-form__error')
            };
        }
        """;
}
