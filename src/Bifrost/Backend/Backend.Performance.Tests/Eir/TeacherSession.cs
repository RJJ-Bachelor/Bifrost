using System.Collections.Concurrent;
using Backend.Performance.Tests.Configuration;
using Backend.Performance.Tests.Infrastructure;
using Backend.Performance.Tests.Reporting;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Domain.Entities;
using EirService.Sessions.Application.Features.Teacher.Common;
using Microsoft.AspNetCore.SignalR.Client;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace Backend.Performance.Tests.Eir;

/// <summary>A distinct, gateway-validated teacher JWT and its own notification connection.</summary>
public sealed class TeacherSession : IAsyncDisposable
{
    private readonly int index;
    private readonly GatewayClient client;
    private readonly IReadOnlyList<SessionResult> lessons;
    private readonly PerformanceOptions options;
    private readonly MetricsCollector metrics;
    private readonly NotificationTracker notifications;
    private readonly IReadOnlySet<Guid> ownedSessions;
    private readonly object disposalSync = new();
    private readonly CancellationTokenSource reloadCancellation = new();
    private readonly SemaphoreSlim answerGate = new(1, 1);
    private readonly ConcurrentBag<Task> answerTasks = new();
    private HubConnection? hub;
    private int connected;
    private Task? disposal;

    public TeacherSession(int index, GatewayClient client, IReadOnlyList<SessionResult> lessons,
        PerformanceOptions options, MetricsCollector metrics, NotificationTracker notifications,
        IReadOnlySet<Guid> ownedSessions)
    {
        this.index = index;
        this.client = client;
        this.lessons = lessons;
        this.options = options;
        this.metrics = metrics;
        this.notifications = notifications;
        this.ownedSessions = ownedSessions;
    }

    public string UserId => client.UserId ?? throw new InvalidOperationException("Teacher has not authenticated.");
    public bool IsConnected => Volatile.Read(ref connected) == 1;
    private string Actor => $"teacher-{index + 1}";

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await client.AuthenticateTeacherAsync(cancellationToken);
        hub = new HubConnectionBuilder()
            .WithUrl(new Uri(options.GatewayUrl, "/api/teachers/notifications"), connection =>
            {
                connection.Cookies = client.Cookies;
                connection.HttpMessageHandlerFactory = inner => new SignalRHttpMetricsHandler(inner, metrics, Actor,
                    () => cancellationToken.IsCancellationRequested || reloadCancellation.IsCancellationRequested);
                if (client.Http.DefaultRequestHeaders.Authorization?.Parameter is { } token)
                    connection.AccessTokenProvider = () => Task.FromResult<string?>(token);
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
        notifications.TeacherReady(UserId);
    }

    /// <summary>Process a lesson after a notification marks its queue dirty.</summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, reloadCancellation.Token);
        cancellationToken = linked.Token;
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConnected) throw new InvalidOperationException($"{Actor} has no active SignalR connection.");
    }

    private void ReceiveNotification(NotificationDeliveryRequestedMessage message)
    {
        notifications.Observe(message, UserId);
        metrics.NotificationReceived(Actor, message.HelpRequestId?.ToString(), message.EventType.ToString());
        if (message.EventType != NotificationEventType.HelpRequestCreated ||
            message.SessionId is not { } sessionId || !ownedSessions.Contains(sessionId))
            return;

        answerTasks.Add(AnswerNextAsync(sessionId, reloadCancellation.Token));
    }

    private async Task<bool> AnswerNextAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var acquired = false;
        try
        {
            await answerGate.WaitAsync(cancellationToken);
            acquired = true;
            var request = await client.TakeNextAsync(sessionId, cancellationToken);
            if (request is null || request.StudentId is null) return false;

            using var expected = notifications.Expect(PerformanceRequirements.AnswerOperation,
                request.StudentId, sessionId, "Answered", request.Id, NotificationEventType.HelpRequestAnswered);
            await client.AnswerAsync(request.Id, $"Performance-svar fra {Actor} til {request.DisplayNumber}.",
                cancellationToken, expected.MarkHttpSent);
            if (!await expected.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken))
                metrics.RecordFailure(PerformanceRequirements.AnswerOperation,
                    "Matching HelpRequestAnswered notification exceeded ten seconds.", request.StudentId);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            metrics.RecordFailure(PerformanceRequirements.AnswerOperation,
                $"{exception.GetType().Name}: {exception.Message}", Actor);
            return false;
        }
        finally
        {
            if (acquired) answerGate.Release();
        }
    }

    public async Task DrainAnswersAsync(CancellationToken cancellationToken)
    {
        foreach (var sessionId in ownedSessions)
        {
            while (await AnswerNextAsync(sessionId, cancellationToken))
            {
            }
        }

        while (true)
        {
            var snapshot = answerTasks.ToArray();
            if (snapshot.Length == 0) return;
            await Task.WhenAll(snapshot).WaitAsync(TimeSpan.FromSeconds(options.NotificationTimeoutSeconds),
                cancellationToken);
            if (snapshot.Length == answerTasks.Count) return;
        }
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
        answerGate.Dispose();
        // Leave the cancelled source readable by a final racing NBomber iteration.
        // The environment owns teacher HTTP clients, also used for final authorized cleanup.
    }
}
