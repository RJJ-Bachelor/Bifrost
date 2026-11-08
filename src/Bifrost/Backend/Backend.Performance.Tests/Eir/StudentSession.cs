using Backend.Performance.Tests.Configuration;
using Backend.Performance.Tests.Infrastructure;
using Backend.Performance.Tests.Reporting;
using EirService.HelpRequests.Domain.Entities;
using EirService.Sessions.Application.Features.Teacher.Common;
using Microsoft.AspNetCore.SignalR.Client;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace Backend.Performance.Tests.Eir;

/// <summary>One real student identity, HTTP cookie jar and persistent notification connection.</summary>
public sealed class StudentSession : IAsyncDisposable
{
    private readonly int index;
    private readonly GatewayClient client;
    private readonly SessionResult lesson;
    private readonly PerformanceOptions options;
    private readonly MetricsCollector metrics;
    private readonly NotificationTracker notifications;
    private readonly string primaryTeacherUserId;
    private readonly IReadOnlyList<string> teacherUserIds;
    private readonly bool active;
    private readonly object disposalSync = new();
    private readonly CancellationTokenSource reloadCancellation = new();
    private HubConnection? hub;
    private int connected;
    private int requestsCreated;
    private int sequence;
    private Task? disposal;

    public StudentSession(int index, GatewayClient client, SessionResult lesson, PerformanceOptions options,
        MetricsCollector metrics, NotificationTracker notifications, bool active, IReadOnlyList<string> teacherUserIds)
    {
        this.index = index;
        this.client = client;
        this.lesson = lesson;
        this.options = options;
        this.metrics = metrics;
        this.notifications = notifications;
        this.active = active;
        this.teacherUserIds = teacherUserIds;
        primaryTeacherUserId = teacherUserIds[0];
    }

    public string UserId => client.UserId ?? throw new InvalidOperationException("Student has not authenticated.");
    public Guid LessonId => lesson.Id;
    public bool IsConnected => Volatile.Read(ref connected) == 1;
    private string Actor => $"student-{index:D4}";

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await client.AuthenticateStudentAsync(cancellationToken);
        await client.JoinStudentAsync(lesson.Code, cancellationToken);
        hub = new HubConnectionBuilder()
            .WithUrl(new Uri(options.GatewayUrl, "/api/students/notifications"), connection =>
            {
                // This is the very same container used by HTTP, including the server-issued cookie.
                connection.Cookies = client.Cookies;
                connection.HttpMessageHandlerFactory = inner => new SignalRHttpMetricsHandler(client.CreateSignalRHttpHandler(inner), metrics, Actor,
                    () => cancellationToken.IsCancellationRequested || reloadCancellation.IsCancellationRequested);
            })
            .WithAutomaticReconnect()
            .Build();
        hub.On<NotificationDeliveryRequestedMessage>("NotificationReceived", ReceiveNotification);
        hub.Reconnecting += exception =>
        {
            if (Interlocked.Exchange(ref connected, 0) == 1)
                metrics.ConnectionReconnecting(Actor, exception?.GetType().Name);
            return Task.CompletedTask;
        };
        hub.Reconnected += _ =>
        {
            if (Interlocked.Exchange(ref connected, 1) == 0) metrics.ConnectionReconnected(Actor);
            return Task.CompletedTask;
        };
        hub.Closed += exception =>
        {
            if (Interlocked.Exchange(ref connected, 0) == 1) metrics.ConnectionClosed(Actor, exception?.GetType().Name);
            return Task.CompletedTask;
        };
        var connectStarted = metrics.Capture();
        var connectSuccess = false;
        string? connectError = null;
        try
        {
            await hub.StartAsync(cancellationToken);
            connectSuccess = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            connectError = "CallerCancellation";
            throw;
        }
        catch (Exception exception)
        {
            connectError = exception.GetType().Name;
            throw;
        }
        finally
        {
            metrics.RecordOperation(connectError == "CallerCancellation" ? "cancelled" : "connection",
                "signalr.connect", connectStarted, connectSuccess, actor: Actor, error: connectError);
        }
        if (Interlocked.Exchange(ref connected, 1) == 0) metrics.ConnectionOpened(Actor);
    }

    /// <summary>One business action; the NBomber caller supplies the configurable think time.</summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, reloadCancellation.Token);
        cancellationToken = linked.Token;
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConnected) throw new InvalidOperationException($"{Actor} has no active SignalR connection.");
        if (!active) return; // Listening clients still perform the real notification-triggered HTTP reloads.
        if (Interlocked.CompareExchange(ref requestsCreated, 1, 0) != 0) return;

        if (options.RequestStartSpreadSeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(Random.Shared.NextDouble() * options.RequestStartSpreadSeconds),
                cancellationToken);

        // The frontend submits directly from the session page. It does not poll the queue
        // immediately before creating a request; the queue page loads after the POST succeeds.
        using var initial = await notifications.BeginInitialQueueCreationAsync(lesson.Id, cancellationToken);
        if (initial is not null)
        {
            await notifications.WaitForTeacherReadyAsync(primaryTeacherUserId, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(options.RequestTimeoutSeconds), cancellationToken);
        }
        using var expected = initial is not null
            ? notifications.Expect("queue.first-to-teacher", primaryTeacherUserId, lesson.Id, "Created",
                eventType: NotificationEventType.HelpRequestCreated, allowedRecipients: teacherUserIds)
            : null;
        var sent = false;
        try
        {
            var helpType = Random.Shared.NextDouble() < options.DigitalRequestFraction ? HelpType.Digital : HelpType.Physical;
            var created = await client.CreateHelpRequestAsync(lesson.Id,
                $"Performance: {Actor}, spørgsmål {++sequence}. Hvordan løses opgave {index}-{sequence}?",
                helpType, cancellationToken, () =>
                {
                    sent = true;
                    expected?.MarkHttpSent();
                });
            expected?.BindRequest(created.Id);
            notifications.RegisterCreatedRequest(created.Id);
        }
        finally
        {
            // Release before awaiting delivery: later student registrations remain concurrent.
            // A failed POST may already have committed, so do not invent a second first request.
            if (sent) initial?.Finish();
            initial?.Dispose();
        }
        if (expected is not null && !await expected.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken))
            throw new TimeoutException("R2: the first queue request did not reach an authorized teacher within ten seconds of HTTP send.");
    }

    private void ReceiveNotification(NotificationDeliveryRequestedMessage message)
    {
        notifications.Observe(message, UserId); // Capture delivery before any reload, rendering substitute or scheduling.
        metrics.NotificationReceived(Actor, message.HelpRequestId?.ToString(), message.EventType.ToString());
        if (message.SessionId != lesson.Id) return;
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalSync) return new ValueTask(disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await reloadCancellation.CancelAsync();
        if (hub is not null)
        {
            metrics.ConnectionCloseScheduled(Actor);
            try { await hub.StopAsync(); }
            finally { await hub.DisposeAsync(); }
        }
        if (Interlocked.Exchange(ref connected, 0) == 1) metrics.ConnectionClosed(Actor, "Scheduled disconnect.");
        // Keep the cancellation source readable for a final racing NBomber iteration.
        // The runner owns HTTP client disposal after the run and cleanup.
    }
}
