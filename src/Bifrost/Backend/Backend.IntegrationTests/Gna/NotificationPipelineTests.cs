using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using FastEndpoints;
using GnaService.Api.Hubs;
using GnaService.Application.Notifications;
using GnaService.Application.Repositories;
using GnaService.Application.Services.Messaging;
using GnaService.Domain.Entities;
using GnaService.Infrastructure;
using GnaService.Infrastructure.Persistence;
using GnaService.Infrastructure.Persistence.Outbox;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Api.Authentication;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Application.Persistence;
using Shared.Domain.Notifications;
using Shared.Infrastructure.Persistence.Outbox;

namespace Backend.IntegrationTests.Gna;

[Collection("Backend hosts")]
public sealed class NotificationPipelineTests : IAsyncLifetime
{
    private static readonly ConditionalWeakTable<WebSocket, BufferedFrames> ReceivedFrames = new();
    private readonly SqliteTestDatabase database = new();
    private readonly TestAuthentication authentication = new();
    private readonly RecordingPushSender push = new();
    private WebApplication app = null!;
    private const string Student = "11111111111111111111111111111111";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:gna"] = "Host=unused;Database=unused",
            ["ConnectionStrings:rabbitmq"] = "amqp://unused"
        });
        builder.Services.AddGnaInfrastructure(builder.Configuration);
        database.Register<GnaNotificationDbContext>(builder.Services);
        builder.Services.AddNotificationRealtime();
        authentication.Register(builder);
        builder.Services.AddMessaging(typeof(NotificationMessageHandler).Assembly);
        builder.Services.RemoveAll<IPushNotificationSender>();
        builder.Services.AddSingleton<IPushNotificationSender>(push);
        builder.Services.RemoveAll<IHostedService>();
        app = builder.Build();
        app.Services.UseMessaging();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapNotificationHubs();
        await app.StartAsync();
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Database.EnsureCreatedAsync();
    }

    [Fact]
    public async Task IncomingMessagePersistsOnceAndDeliversCommittedOutboxThroughRealSignalR()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        using var socket = await ConnectStudentAsync(Student, token);
        var incoming = Incoming();
        await HandleAsync(incoming, token);
        await HandleAsync(incoming, token);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
            var notification = Assert.Single(await db.Notifications.AsNoTracking().ToArrayAsync(token));
            Assert.Equal(incoming.Id, notification.Id);
            Assert.Equal(incoming.Payload.HelpRequestId, notification.HelpRequestId);
            Assert.Equal(NotificationEventType.HelpRequestAnswered, notification.EventType);
            Assert.Null(Assert.Single(await db.Outbox.ToArrayAsync(token)).PublishedAtUtc);
        }

        using var dispatcher = ActivatorUtilities.CreateInstance<NotificationOutboxDispatcher>(app.Services);
        await dispatcher.DispatchPendingAsync(token);
        using var frame = JsonDocument.Parse((await ReadFrameAsync(socket, token)).TrimEnd('\u001e'));
        Assert.Equal("NotificationReceived", frame.RootElement.GetProperty("target").GetString());
        var delivered = frame.RootElement.GetProperty("arguments")[0];
        Assert.Equal(incoming.Id, delivered.GetProperty("notificationId").GetGuid());
        Assert.Equal(incoming.Payload.Messages, delivered.GetProperty("message").GetString());
        Assert.Equal(Student, delivered.GetProperty("userId").GetString());
        Assert.Empty(push.Messages);
        await dispatcher.DispatchPendingAsync(token);
        await using var verification = app.Services.CreateAsyncScope();
        Assert.NotNull((await verification.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Outbox.SingleAsync(token)).PublishedAtUtc);
        using var noDuplicate = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadFrameAsync(socket, noDuplicate.Token));
    }

    [Fact]
    public async Task FailedOutboxInsertRollsBackNotificationAndPreservesDomainEventForRetry()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_outbox BEFORE INSERT ON outbox BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        var notification = Notification.Create(Guid.NewGuid(), Student, "Atomic notification");
        var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<INotificationRepository>>();
        Assert.True(await repository.TryAddAsync(notification, default));
        await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.SaveChangesAsync());
        Assert.Single(notification.GetDomainEvents());
        Assert.Empty(db.ChangeTracker.Entries<OutboxMessage>());
        await using (var verification = app.Services.CreateAsyncScope())
        {
            var saved = verification.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
            Assert.Empty(await saved.Notifications.ToArrayAsync());
            Assert.Empty(await saved.Outbox.ToArrayAsync());
        }
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_outbox;");
        await unitOfWork.SaveChangesAsync();
        Assert.Empty(notification.GetDomainEvents());
        Assert.Single(await db.Notifications.ToArrayAsync());
        Assert.Single(await db.Outbox.ToArrayAsync());
    }

    [Theory]
    [InlineData(NotificationDeliveryType.RealtimeOrPush, 1)]
    [InlineData(NotificationDeliveryType.RealtimeOnly, 0)]
    public async Task OfflineDeliveryUsesConfiguredFallbackAndDoesNotReplay(NotificationDeliveryType deliveryType, int expectedPush)
    {
        await HandleAsync(Incoming(deliveryType), default);
        using var dispatcher = ActivatorUtilities.CreateInstance<NotificationOutboxDispatcher>(app.Services);
        await dispatcher.DispatchPendingAsync(default);
        await dispatcher.DispatchPendingAsync(default);
        Assert.Equal(expectedPush, push.Messages.Count);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.NotNull((await scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Outbox.SingleAsync()).PublishedAtUtc);
    }

    [Fact]
    public async Task BrokenOutboxPayloadDoesNotBlockValidDeliveryAndCanBeRepaired()
    {
        var broken = Incoming();
        var valid = Incoming();
        await HandleAsync(broken, default);
        await HandleAsync(valid, default);
        string original;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
            original = (await db.Outbox.SingleAsync(row => row.MessageId == broken.Id)).Payload;
            await db.Outbox.Where(row => row.MessageId == broken.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Payload, "{"));
        }
        using var dispatcher = ActivatorUtilities.CreateInstance<NotificationOutboxDispatcher>(app.Services);
        await dispatcher.DispatchPendingAsync(default);
        Assert.Equal(valid.Id, Assert.Single(push.Messages).NotificationId);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
            var failed = await db.Outbox.SingleAsync(row => row.MessageId == broken.Id);
            Assert.Null(failed.PublishedAtUtc);
            Assert.NotNull(failed.LastAttemptAtUtc);
            await db.Outbox.Where(row => row.MessageId == broken.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Payload, original));
        }
        await dispatcher.DispatchPendingAsync(default);
        Assert.Equal(broken.Id, push.Messages.Last().NotificationId);
        await using var check = app.Services.CreateAsyncScope();
        Assert.All(await check.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Outbox.ToArrayAsync(), row => Assert.NotNull(row.PublishedAtUtc));
    }

    [Fact]
    public async Task PushSubscriptionsUpdateExistingKeysAndRemoveOnlyTheOwnersEndpoint()
    {
        const string endpoint = "https://push.example.test/subscription";
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IPushSubscriptionRepository>();
            await repository.SaveAsync(PushSubscription.Create(Student, endpoint, "old-key", "old-auth"), default);
            await repository.SaveAsync(PushSubscription.Create(Student, endpoint, "new-key", "new-auth"), default);
            await repository.SaveAsync(PushSubscription.Create("another-user", endpoint, "other-key", "other-auth"), default);
        }
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IPushSubscriptionRepository>();
            var saved = Assert.Single(await repository.GetForUserAsync(Student, default));
            Assert.Equal("new-key", saved.P256dh);
            Assert.Equal("new-auth", saved.Auth);
            await repository.RemoveAsync(Student, endpoint, default);
        }
        await using var verification = app.Services.CreateAsyncScope();
        var remaining = Assert.Single(await verification.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().PushSubscriptions.ToArrayAsync());
        Assert.Equal("another-user", remaining.UserId.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StudentHubRejectsAbsentOrTamperedCookie(bool tampered)
    {
        using var client = app.GetTestClient();
        if (tampered) client.DefaultRequestHeaders.Add("Cookie", StudentCookieHandler.CookieName + "=tampered");
        using var response = await client.PostAsync(NotificationHubAuthentication.StudentPath + "/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static MessageEnvelope<NotificationCreatedMessage> Incoming(NotificationDeliveryType deliveryType = NotificationDeliveryType.RealtimeOrPush) =>
        new(Guid.NewGuid(), new(Student, "Teacher answered", Guid.NewGuid(), 2, Guid.NewGuid(), "Answered", "Completed", DeliveryType: deliveryType));

    private async Task HandleAsync(MessageEnvelope<NotificationCreatedMessage> incoming, CancellationToken token)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INotificationMessageHandler>().HandleAsync(incoming, token);
    }

    private async Task<WebSocket> ConnectStudentAsync(string subject, CancellationToken token)
    {
        var client = app.GetTestServer().CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Cookie = StudentCookieHandler.CookieName + "=" +
            app.Services.GetRequiredService<StudentIdentityCookie>().Protect(subject);
        var socket = await client.ConnectAsync(new Uri("ws://localhost" + NotificationHubAuthentication.StudentPath), token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, token);
        Assert.Equal("{}\u001e", await ReadFrameAsync(socket, token));
        while (!app.Services.GetRequiredService<IConnectionRegistry>().IsConnected(subject)) await Task.Delay(10, token);
        return socket;
    }

    private static async Task<string> ReadFrameAsync(WebSocket socket, CancellationToken token)
    {
        var frames = ReceivedFrames.GetValue(socket, _ => new BufferedFrames());
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var separator = frames.Pending.IndexOf(0x1e);
            if (separator >= 0)
            {
                var frame = frames.Pending.GetRange(0, separator + 1).ToArray();
                frames.Pending.RemoveRange(0, separator + 1);
                return Encoding.UTF8.GetString(frame);
            }

            var result = await socket.ReceiveAsync(new ArraySegment<byte>(frames.ReadBuffer), token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("The SignalR connection closed before a complete frame was received.");
            frames.Pending.AddRange(frames.ReadBuffer.AsSpan(0, result.Count).ToArray());
        }
    }

    private sealed class BufferedFrames
    {
        public byte[] ReadBuffer { get; } = new byte[8192];
        public List<byte> Pending { get; } = [];
    }

    public async Task DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
        await database.DisposeAsync();
    }

    private sealed class RecordingPushSender : IPushNotificationSender
    {
        public List<NotificationDeliveryRequestedMessage> Messages { get; } = [];
        public Task<bool> TrySendAsync(NotificationDeliveryRequestedMessage notification, CancellationToken cancellationToken)
        {
            Messages.Add(notification);
            return Task.FromResult(true);
        }
    }
}
