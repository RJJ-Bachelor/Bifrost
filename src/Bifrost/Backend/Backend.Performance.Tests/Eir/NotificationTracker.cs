using System.Collections.Concurrent;
using System.Diagnostics;
using Backend.Performance.Tests.Reporting;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace Backend.Performance.Tests.Eir;

/// <summary>
/// Correlates a gateway HTTP send with the receiving client's SignalR callback. Both timestamps
/// use Stopwatch, so neither a server timestamp nor clocks on different machines enter the result.
/// </summary>
public sealed class NotificationTracker(MetricsCollector metrics)
{
    private readonly ConcurrentDictionary<long, Expectation> pending = new();
    private readonly ConcurrentDictionary<Guid, InitialQueueState> initialQueues = new();
    private readonly ConcurrentDictionary<Guid, byte> createdRequests = new();
    private readonly ConcurrentDictionary<Guid, byte> answeredNotifications = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> teachersReady = new(StringComparer.Ordinal);
    private long nextId;

    public void TeacherReady(string userId) => teachersReady.GetOrAdd(userId,
        _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    public Task WaitForTeacherReadyAsync(string userId, CancellationToken cancellationToken) =>
        teachersReady.GetOrAdd(userId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously))
            .Task.WaitAsync(cancellationToken);

    public int CreatedRequestCount => createdRequests.Count;
    public int AnswerNotificationCount => createdRequests.Keys.Count(answeredNotifications.ContainsKey);

    public async Task WaitForCreatedRequestsAsync(int expected, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (CreatedRequestCount < expected)
        {
            if (Stopwatch.GetTimestamp() >= deadline) break;
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    public void RegisterCreatedRequest(Guid requestId)
    {
        createdRequests.TryAdd(requestId, 0);
        if (answeredNotifications.ContainsKey(requestId)) return;
    }

    public async Task WaitForAllAnswerNotificationsAsync(int expected, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (CreatedRequestCount < expected || AnswerNotificationCount < expected)
        {
            if (Stopwatch.GetTimestamp() >= deadline) break;
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        var missing = createdRequests.Keys.Where(id => !answeredNotifications.ContainsKey(id)).ToArray();
        foreach (var requestId in missing)
            metrics.RecordOperation("notification", PerformanceRequirements.AnswerOperation,
                metrics.Capture(), false, correlationId: requestId.ToString(),
                error: "No matching HelpRequestAnswered notification was observed during the drain window.");
        if (CreatedRequestCount != expected || missing.Length > 0)
            metrics.RecordFailure("k3_accounting",
                $"Expected {expected} created requests with matching student answer notifications; " +
                $"created {CreatedRequestCount}, notifications {AnswerNotificationCount}.", "k3");
    }

    public bool InitialQueueCreationFinished(Guid sessionId) => initialQueues.TryGetValue(sessionId, out var state) &&
        Volatile.Read(ref state.Finished) == 1;

    /// <summary>
    /// Only the guaranteed initial creation in a fresh test session is an R2 probe. Hold its gate
    /// through the POST response, so a second student cannot alter the server's queue-start decision.
    /// Later creations do not acquire this gate and remain concurrent.
    /// </summary>
    public async Task<InitialQueueCreation?> BeginInitialQueueCreationAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var state = initialQueues.GetOrAdd(sessionId, _ => new());
        if (Volatile.Read(ref state.Finished) == 1) return null;
        await state.Gate.WaitAsync(cancellationToken);
        if (Volatile.Read(ref state.Finished) == 1)
        {
            state.Gate.Release();
            return null;
        }
        return new InitialQueueCreation(state);
    }

    internal sealed class InitialQueueState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int Finished;
    }

    public sealed class InitialQueueCreation : IDisposable
    {
        private readonly InitialQueueState state;
        private int disposed;

        internal InitialQueueCreation(InitialQueueState state) => this.state = state;

        // An uncertain HTTP outcome must never cause the next student to be mislabelled as first.
        public void Finish() => Volatile.Write(ref state.Finished, 1);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) state.Gate.Release();
        }
    }

    public Expectation Expect(string operation, string recipient, Guid sessionId, string change,
        Guid? requestId = null, NotificationEventType? eventType = null,
        IReadOnlyCollection<string>? allowedRecipients = null, bool allowMissingRequestId = false)
    {
        var id = Interlocked.Increment(ref nextId);
        var expectation = new Expectation(this, metrics, id, operation, recipient, sessionId,
            change, requestId, eventType, allowedRecipients, allowMissingRequestId);
        pending[id] = expectation;
        return expectation;
    }

    public void Observe(NotificationDeliveryRequestedMessage message, string actualRecipient)
    {
        // Public refreshes have no request ID and cannot complete a private delivery probe.
        // Trust the authenticated receiving connection, not a recipient claimed by the payload.
        if (message.HelpRequestId is null || string.IsNullOrEmpty(actualRecipient) ||
            message.UserId != actualRecipient) return;
        if (message.EventType == NotificationEventType.HelpRequestAnswered)
            answeredNotifications.TryAdd(message.HelpRequestId.Value, 0);
        var receivedAt = Stopwatch.GetTimestamp();
        foreach (var expectation in pending.Values)
            expectation.Observe(message, receivedAt);
    }

    public sealed class Expectation : IDisposable
    {
        private readonly NotificationTracker owner;
        private readonly MetricsCollector metrics;
        private readonly long id;
        private readonly string operation;
        private readonly string recipient;
        private readonly HashSet<string>? allowedRecipients;
        private readonly Guid sessionId;
        private readonly string change;
        private readonly NotificationEventType? eventType;
        private readonly bool allowMissingRequestId;
        private readonly object sync = new();
        private readonly Dictionary<Guid, (long Timestamp, string Recipient)> earlyMessages = new();
        private readonly TaskCompletionSource<(long Timestamp, string Recipient)> received =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private OperationStamp started;
        private Guid? requestId;
        private bool armed;
        private bool disposed;

        internal Expectation(NotificationTracker owner, MetricsCollector metrics, long id,
            string operation, string recipient, Guid sessionId, string change, Guid? requestId,
            NotificationEventType? eventType, IReadOnlyCollection<string>? allowedRecipients,
            bool allowMissingRequestId)
        {
            this.owner = owner;
            this.metrics = metrics;
            this.id = id;
            this.operation = operation;
            this.recipient = recipient;
            this.allowedRecipients = allowedRecipients?.ToHashSet(StringComparer.Ordinal);
            this.sessionId = sessionId;
            this.change = change;
            this.requestId = requestId;
            this.eventType = eventType;
            this.allowMissingRequestId = allowMissingRequestId;
        }

        // Passed to GatewayClient.beforeSend: registered before the send, armed at the actual send.
        public void MarkHttpSent()
        {
            lock (sync)
            {
                started = metrics.Capture();
                armed = true;
            }
        }

        // POST create/take-next only reveals its entity ID in the HTTP response. A notification
        // may arrive first, so retain candidates and bind to the exact ID after deserialization.
        public void BindRequest(Guid value)
        {
            lock (sync)
            {
                requestId = value;
                if (earlyMessages.TryGetValue(value, out var delivery)) received.TrySetResult(delivery);
                earlyMessages.Clear();
            }
        }

        public void RecordHttpFailure(string error, bool cancelled = false)
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                // Failure before the actual send has no HTTP-to-notification interval.
                if (armed)
                    metrics.RecordOperation(cancelled ? "cancelled" : "notification", operation, started,
                        false, actor: recipient, correlationId: requestId?.ToString(), error: error);
            }
            owner.pending.TryRemove(id, out _);
        }

        internal void Observe(NotificationDeliveryRequestedMessage message, long timestamp)
        {
            if ((allowedRecipients is null ? message.UserId != recipient : !allowedRecipients.Contains(message.UserId)) ||
                message.SessionId != sessionId || message.Change != change ||
                message.HelpRequestId is null && !allowMissingRequestId ||
                eventType is { } expected && message.EventType != expected)
                return;

            lock (sync)
            {
                if (!armed || disposed || timestamp < started.Timestamp) return;
                if (requestId is { } expectedId)
                {
                    if (message.HelpRequestId == expectedId) received.TrySetResult((timestamp, message.UserId));
                }
                else if (allowMissingRequestId && message.HelpRequestId is null)
                    received.TrySetResult((timestamp, message.UserId));
                else if (message.HelpRequestId is { } messageId &&
                    (!earlyMessages.TryGetValue(messageId, out var earlier) || timestamp < earlier.Timestamp))
                    earlyMessages[messageId] = (timestamp, message.UserId);
            }
        }

        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (!armed) throw new InvalidOperationException("Notification expectation was never armed by an HTTP send.");
            try
            {
                // HTTP can consume part or all of the budget; it must not restart the deadline.
                while (!received.Task.IsCompletedSuccessfully)
                {
                    var remaining = timeout - Stopwatch.GetElapsedTime(started.Timestamp);
                    if (remaining <= TimeSpan.Zero) throw new TimeoutException();
                    // Timer delays can round down. Recheck the monotonic deadline before failing.
                    try { await received.Task.WaitAsync(TimeSpan.FromMilliseconds(Math.Ceiling(remaining.TotalMilliseconds)), cancellationToken); }
                    catch (TimeoutException) { }
                }
                var delivery = received.Task.Result;
                var withinDeadline = Stopwatch.GetElapsedTime(started.Timestamp, delivery.Timestamp) <= timeout;
                metrics.RecordOperation("notification", operation, started, withinDeadline, actor: delivery.Recipient,
                    correlationId: requestId?.ToString(), completedTimestamp: delivery.Timestamp,
                    error: withinDeadline ? null : "Matching SignalR notification arrived after the deadline from HTTP send.");
                return withinDeadline;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                metrics.RecordOperation("cancelled", operation, started, false, actor: recipient,
                    correlationId: requestId?.ToString(), error: "CallerCancellation");
                throw;
            }
            catch (TimeoutException)
            {
                metrics.RecordOperation("notification", operation, started, false, actor: recipient,
                    correlationId: requestId?.ToString(), error: "No matching SignalR notification before timeout.");
                return false;
            }
            finally { Dispose(); }
        }

        public void Dispose()
        {
            lock (sync) disposed = true;
            owner.pending.TryRemove(id, out _);
        }
    }
}
