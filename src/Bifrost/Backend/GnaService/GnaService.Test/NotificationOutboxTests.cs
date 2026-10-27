using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GnaService.Api.Hubs;
using GnaService.Application.Notifications;
using GnaService.Application.Services.Messaging;
using GnaService.Infrastructure;
using GnaService.Infrastructure.Persistence;
using GnaService.Infrastructure.Persistence.Outbox;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Api.Authentication;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Domain.Notifications;
using Shared.Infrastructure.Persistence.Outbox;

namespace GnaService.Test;

public sealed class NotificationOutboxRegistrationTests
{
    [Fact]
    public async Task InfrastructureUsesADirectDeliveryWorkerInsteadOfABrokerDeliverySubscriber()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:gna"] = "Host=unused;Database=unused",
            ["ConnectionStrings:rabbitmq"] = "amqp://unused"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddGnaInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        var workers = provider.GetServices<IHostedService>().Select(worker => worker.GetType()).ToArray();

        Assert.Contains(typeof(NotificationOutboxDispatcher), workers);
        Assert.Contains(workers, worker => worker.Name == "NotificationMessageSubscriber");
        Assert.DoesNotContain(workers, worker => worker.Name == "NotificationDeliverySubscriber");
        Assert.DoesNotContain(typeof(OutboxDispatcher<GnaNotificationDbContext>), workers);
    }
}

public sealed partial class NotificationPersistenceTests
{
    [GnaPostgresFact]
    public async Task DirectOutboxWorkerDeliversACommittedNotificationOverSignalRWithoutRabbitMq()
    {
        const string student = "11111111111111111111111111111111";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        var cookie = app.Services.GetRequiredService<StudentIdentityCookie>().Protect(student);
        var client = app.GetTestServer().CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Cookie = StudentCookieHandler.CookieName + "=" + cookie;
        using var socket = await client.ConnectAsync(new Uri("ws://localhost" + NotificationHubAuthentication.StudentPath), token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"),
            WebSocketMessageType.Text, true, token);
        Assert.Equal("{}\u001e", await ReadOutboxFrameAsync(socket, token));
        while (!app.Services.GetRequiredService<IConnectionRegistry>().IsConnected(student))
            await Task.Delay(10, token);

        var incoming = new MessageEnvelope<NotificationCreatedMessage>(Guid.NewGuid(), new(student, "Queue changed",
            Guid.NewGuid(), 4, Change: "Created", EventType: NotificationEventType.SessionChanged,
            DeliveryType: NotificationDeliveryType.RealtimeOnly));
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificationMessageHandler>().HandleAsync(incoming, token);

        using var worker = ActivatorUtilities.CreateInstance<NotificationOutboxDispatcher>(app.Services);
        await worker.DispatchPendingAsync(token);

        using var frame = JsonDocument.Parse((await ReadOutboxFrameAsync(socket, token)).TrimEnd('\u001e'));
        Assert.Equal("NotificationReceived", frame.RootElement.GetProperty("target").GetString());
        var notification = frame.RootElement.GetProperty("arguments")[0];
        Assert.Equal(incoming.Id, notification.GetProperty("notificationId").GetGuid());
        Assert.Equal(incoming.Payload.SessionId, notification.GetProperty("sessionId").GetGuid());
        Assert.Equal("SessionChanged", notification.GetProperty("eventType").GetString());
        Assert.Equal("RealtimeOnly", notification.GetProperty("deliveryType").GetString());
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
            Assert.Single(await db.Notifications.ToArrayAsync(token));
            Assert.NotNull((await db.Outbox.SingleAsync(token)).PublishedAtUtc);
        }

        await worker.DispatchPendingAsync(token);
        using var noDuplicate = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadOutboxFrameAsync(socket, noDuplicate.Token));
    }

    [GnaPostgresFact]
    public async Task DirectWorkerProcessesLegacyPendingPayloadsAndDoesNotReplayCompletedDeliveries()
    {
        var old = new NotificationDeliveryRequestedMessage(Guid.NewGuid(), "student", "Legacy answer", DateTimeOffset.UtcNow);
        var complete = new NotificationDeliveryRequestedMessage(Guid.NewGuid(), "student", "Already delivered", DateTimeOffset.UtcNow);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
            var completed = StageDelivery(complete);
            completed.MarkPublished(DateTimeOffset.UtcNow);
            db.Outbox.AddRange(StageDelivery(old), completed);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox SET \"Payload\" = (\"Payload\"::jsonb - 'deliveryType' - 'eventType')::text WHERE \"MessageId\" = {old.NotificationId}");
        }
        var realtime = new RecordingDeliverySender(true);
        var push = new RecordingDeliverySender(true);
        await using var services = BuildDeliveryWorkerServices(realtime, push);
        using var worker = ActivatorUtilities.CreateInstance<NotificationOutboxDispatcher>(services);

        await worker.DispatchPendingAsync(default);
        await worker.DispatchPendingAsync(default);

        var delivered = Assert.Single(realtime.Messages);
        Assert.Equal(old.NotificationId, delivered.NotificationId);
        Assert.Equal(NotificationDeliveryType.RealtimeOrPush, delivered.DeliveryType);
        Assert.Empty(push.Messages);
        await using var check = app.Services.CreateAsyncScope();
        Assert.All(await check.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Outbox.ToArrayAsync(),
            entry => Assert.NotNull(entry.PublishedAtUtc));
    }

    [GnaPostgresFact]
    public async Task MalformedPayloadRemainsPendingWhileOtherDeliveriesProgressAndCanBeRetried()
    {
        var broken = new NotificationDeliveryRequestedMessage(Guid.NewGuid(), "student", "Retry after repair", DateTimeOffset.UtcNow);
        var valid = new NotificationDeliveryRequestedMessage(Guid.NewGuid(), "student", "Unaffected", DateTimeOffset.UtcNow);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
            db.Outbox.AddRange(StageDelivery(broken), StageDelivery(valid));
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox SET \"Payload\" = '{{' WHERE \"MessageId\" = {broken.NotificationId}");
        }
        var realtime = new RecordingDeliverySender(true);
        await using var services = BuildDeliveryWorkerServices(realtime, new RecordingDeliverySender(true));
        using var worker = ActivatorUtilities.CreateInstance<NotificationOutboxDispatcher>(services);

        await worker.DispatchPendingAsync(default);

        Assert.Equal(valid.NotificationId, Assert.Single(realtime.Messages).NotificationId);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
            var failed = await db.Outbox.SingleAsync(message => message.MessageId == broken.NotificationId);
            Assert.Null(failed.PublishedAtUtc);
            Assert.NotNull(failed.LastAttemptAtUtc);
            Assert.NotNull((await db.Outbox.SingleAsync(message => message.MessageId == valid.NotificationId)).PublishedAtUtc);
            var repaired = JsonSerializer.Serialize(broken, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox SET \"Payload\" = {repaired} WHERE \"MessageId\" = {broken.NotificationId}");
        }

        await worker.DispatchPendingAsync(default);

        Assert.Equal(2, realtime.Messages.Count);
        Assert.Equal(broken.NotificationId, realtime.Messages[1].NotificationId);
        await using var check = app.Services.CreateAsyncScope();
        Assert.All(await check.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Outbox.ToArrayAsync(),
            message => Assert.NotNull(message.PublishedAtUtc));
    }

    [GnaPostgresFact]
    public async Task OfflineRealtimeOnlyDeliveryIsCompletedWithoutPushOrLaterReplay()
    {
        var delivery = new NotificationDeliveryRequestedMessage(Guid.NewGuid(), "student", "Queue changed", DateTimeOffset.UtcNow,
            DeliveryType: NotificationDeliveryType.RealtimeOnly);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
            db.Outbox.Add(StageDelivery(delivery));
            await db.SaveChangesAsync();
        }
        var realtime = new RecordingDeliverySender(false);
        var push = new RecordingDeliverySender(true);
        await using var services = BuildDeliveryWorkerServices(realtime, push);
        using var worker = ActivatorUtilities.CreateInstance<NotificationOutboxDispatcher>(services);

        await worker.DispatchPendingAsync(default);
        await worker.DispatchPendingAsync(default);

        Assert.Single(realtime.Messages);
        Assert.Empty(push.Messages);
        await using var check = app.Services.CreateAsyncScope();
        Assert.NotNull((await check.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Outbox.SingleAsync()).PublishedAtUtc);
    }

    [GnaPostgresFact]
    public async Task CancellationDuringDeliveryLeavesTheOutboxPendingWithoutStartingPush()
    {
        var delivery = new NotificationDeliveryRequestedMessage(Guid.NewGuid(), "student", "Answer", DateTimeOffset.UtcNow);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
            db.Outbox.Add(StageDelivery(delivery));
            await db.SaveChangesAsync();
        }
        using var stopping = new CancellationTokenSource();
        var realtime = new CancellingDeliverySender(stopping);
        var push = new RecordingDeliverySender(true);
        await using var services = BuildDeliveryWorkerServices(realtime, push);
        using var worker = ActivatorUtilities.CreateInstance<NotificationOutboxDispatcher>(services);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.DispatchPendingAsync(stopping.Token));

        Assert.Equal(1, realtime.Calls);
        Assert.Empty(push.Messages);
        await using var check = app.Services.CreateAsyncScope();
        var pending = await check.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Outbox.SingleAsync();
        Assert.Null(pending.PublishedAtUtc);
        Assert.Null(pending.LastAttemptAtUtc);
    }

    private ServiceProvider BuildDeliveryWorkerServices(IRealtimeNotificationSender realtime, IPushNotificationSender push)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<GnaNotificationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton(realtime);
        services.AddSingleton(push);
        services.AddScoped<NotificationDispatcher>();
        return services.BuildServiceProvider();
    }

    private static OutboxMessage StageDelivery(NotificationDeliveryRequestedMessage delivery) => OutboxMessage.Create(
        new OutgoingMessage(delivery.NotificationId, delivery, MessageRoutes.Notifications, MessageRoutes.NotificationDeliveryRequested),
        delivery.CreatedAt);

    private static async Task<string> ReadOutboxFrameAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private sealed class RecordingDeliverySender(bool online) : IRealtimeNotificationSender, IPushNotificationSender
    {
        public List<NotificationDeliveryRequestedMessage> Messages { get; } = [];
        public Task<bool> TrySendAsync(NotificationDeliveryRequestedMessage notification, CancellationToken cancellationToken)
        {
            Messages.Add(notification);
            return Task.FromResult(online);
        }
    }

    private sealed class CancellingDeliverySender(CancellationTokenSource stopping) : IRealtimeNotificationSender
    {
        public int Calls { get; private set; }
        public Task<bool> TrySendAsync(NotificationDeliveryRequestedMessage notification, CancellationToken cancellationToken)
        {
            Calls++;
            stopping.Cancel();
            return Task.FromCanceled<bool>(cancellationToken);
        }
    }
}
