using FastEndpoints;
using GnaService.Api.Hubs;
using GnaService.Application.Repositories;
using GnaService.Application.Services.Messaging;
using GnaService.Domain.Entities;
using GnaService.Infrastructure;
using GnaService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shared.Api.Authentication;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GnaService.Application.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Shared.Infrastructure.Persistence.Outbox;
using Shared.Domain.Notifications;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Application.Messaging.Outbox;

namespace GnaService.Test;

public sealed class NotificationPersistenceTests : IAsyncLifetime
{
    private readonly string database = "gna_tests_" + Guid.NewGuid().ToString("N");
    private string connectionString = "";
    private WebApplication app = null!;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("BIFROST_TEST_POSTGRES") is not { } admin) return;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
        await create.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:gna"] = connectionString,
            ["ConnectionStrings:rabbitmq"] = Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ") ?? "amqp://unused-in-tests"
        });
        builder.Services.AddGnaInfrastructure(builder.Configuration);
        builder.Services.AddNotificationRealtime();
        builder.Services.AddBifrostServiceAuthentication(builder.Configuration, builder.Environment);
        builder.Services.PostConfigure<StudentCookieOptions>(StudentCookieHandler.SchemeName,
            options => options.DataProtectionProvider = new EphemeralDataProtectionProvider());
        builder.Services.AddMessaging(typeof(NotificationMessageHandler).Assembly);
        // Database tests exercise persistence/outbox, not broker background jobs.
        builder.Services.RemoveAll<IHostedService>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        app = builder.Build();
        app.Services.UseMessaging();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapNotificationHubs();
        await app.StartAsync();
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Database.MigrateAsync();
    }

    [GnaPostgresFact]
    public async Task NotificationAndDeliveryOutboxAreCommittedTogetherAndRedeliveryIsIgnored()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<INotificationMessageHandler>();
        var incoming = new MessageEnvelope<NotificationCreatedMessage>(Guid.NewGuid(),
            new("student", "Hello", Guid.NewGuid(), 2, Guid.NewGuid(), "Answered", "Completed"));
        await handler.HandleAsync(incoming, default);
        await handler.HandleAsync(incoming, default);
        var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
        db.ChangeTracker.Clear();
        var saved = Assert.Single(await db.Notifications.ToArrayAsync());
        Assert.Equal(incoming.Payload.SessionId, saved.SessionId);
        Assert.Equal(incoming.Payload.HelpRequestId, saved.HelpRequestId);
        Assert.Equal(incoming.Payload.Generation, saved.Generation);
        Assert.Equal(incoming.Payload.Change, saved.Change);
        Assert.Equal(incoming.Payload.Status, saved.Status);
        Assert.Equal(NotificationEventType.HelpRequestAnswered, saved.EventType);
        var outbox = Assert.Single(await db.Outbox.ToArrayAsync());
        Assert.Equal(incoming.Id, outbox.MessageId);
        Assert.Equal(MessageRoutes.NotificationDeliveryRequested, outbox.RoutingKey);
        Assert.Null(outbox.PublishedAtUtc);
        var delivery = JsonSerializer.Deserialize<NotificationDeliveryRequestedMessage>(outbox.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(incoming.Id, delivery.NotificationId);
        Assert.Equal(incoming.Payload.UserId, delivery.UserId);
        Assert.Equal(incoming.Payload.Messages, delivery.Message);
        Assert.Equal(incoming.Payload.SessionId, delivery.SessionId);
        Assert.Equal(incoming.Payload.HelpRequestId, delivery.HelpRequestId);
        Assert.Equal(incoming.Payload.Generation, delivery.Generation);
        Assert.Equal(incoming.Payload.Change, delivery.Change);
        Assert.Equal(incoming.Payload.Status, delivery.Status);
        Assert.Equal(NotificationEventType.HelpRequestAnswered, delivery.EventType);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [GnaPostgresFact]
    public async Task PushSubscriptionsAreStoredAndQueriedByUser()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPushSubscriptionRepository>();
        await repository.SaveAsync(PushSubscription.Create("student", "https://push.example/one", "key", "auth"), default);
        await repository.SaveAsync(PushSubscription.Create("other", "https://push.example/two", "key", "auth"), default);
        await repository.SaveAsync(PushSubscription.Create("student", "https://push.example/one", "new-key", "new-auth"), default);
        var saved = Assert.Single(await repository.GetForUserAsync("student", default));
        Assert.Equal("new-key", saved.P256dh);
        await repository.RemoveAsync("other", "https://push.example/one", default);
        Assert.Single(await repository.GetForUserAsync("student", default));
        await repository.RemoveAsync("student", "https://push.example/one", default);
        Assert.Empty(await repository.GetForUserAsync("student", default));
    }

    [GnaPostgresFact]
    public async Task EventTypeMigrationBackfillsHistoryAndPendingDeliveriesWithoutReplayingPublishedMessages()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
        var cases = new[] {
            ("Created", NotificationEventType.HelpRequestCreated),
            ("StudentMessageAdded", NotificationEventType.StudentMessageAdded),
            ("Started", NotificationEventType.HelpRequestStarted),
            ("Answered", NotificationEventType.HelpRequestAnswered),
            ("FaqCreated", NotificationEventType.FaqCreated),
            ("FaqSuggested", NotificationEventType.FaqAnswered),
            ("Legacy", NotificationEventType.Unknown)
        };
        var records = cases.Select(item => Notification.Create(Guid.NewGuid(), "student", "Message",
            sessionId: Guid.NewGuid(), generation: 3, helpRequestId: Guid.NewGuid(), change: item.Item1,
            status: "AwaitingConfirmation", eventType: item.Item2)).ToArray();
        db.Notifications.AddRange(records);
        var source = records.Single(record => record.EventType == NotificationEventType.FaqAnswered);
        var payload = new NotificationDeliveryRequestedMessage(source.Id, source.UserId.Value, source.Messages.Value,
            source.CreatedAt, source.SessionId, source.HelpRequestId);
        var pending = OutboxMessage.Create(new OutgoingMessage(source.Id, payload, MessageRoutes.Notifications,
            MessageRoutes.NotificationDeliveryRequested), source.CreatedAt);
        var published = OutboxMessage.Create(new OutgoingMessage(source.Id, payload, MessageRoutes.Notifications,
            MessageRoutes.NotificationDeliveryRequested), source.CreatedAt);
        published.MarkPublished(source.CreatedAt);
        db.Outbox.AddRange(pending, published);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE outbox SET "Payload" = ("Payload"::jsonb - 'eventType' - 'generation' - 'change' - 'status')::text;
            """);
        await db.GetService<IMigrator>().MigrateAsync("20261019175409_AddNotificationContext");
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        foreach (var (change, eventType) in cases)
            Assert.Equal(eventType, (await db.Notifications.SingleAsync(notification => notification.Change == change)).EventType);
        var enriched = await db.Outbox.SingleAsync(message => message.Id == pending.Id);
        var result = JsonSerializer.Deserialize<NotificationDeliveryRequestedMessage>(enriched.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(NotificationEventType.FaqAnswered, result.EventType);
        Assert.Equal(source.Generation, result.Generation);
        Assert.Equal(source.Change, result.Change);
        Assert.Equal(source.Status, result.Status);
        Assert.Null(enriched.PublishedAtUtc);
        var historical = await db.Outbox.SingleAsync(message => message.Id == published.Id);
        Assert.NotNull(historical.PublishedAtUtc);
        using var historicalJson = JsonDocument.Parse(historical.Payload);
        Assert.False(historicalJson.RootElement.TryGetProperty("eventType", out _));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [GnaBrokerFact]
    public async Task RabbitMessageCommitsAndReachesTheStudentOverSignalR()
    {
        const string student = "11111111111111111111111111111111";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        var cookie = app.Services.GetRequiredService<StudentIdentityCookie>().Protect(student);
        var socketClient = app.GetTestServer().CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Cookie = StudentCookieHandler.CookieName + "=" + cookie;
        using var socket = await socketClient.ConnectAsync(new Uri("ws://localhost" + NotificationHubAuthentication.StudentPath), token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, token);
        var buffer = new byte[8192];
        await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
        while (!app.Services.GetRequiredService<IConnectionRegistry>().IsConnected(student)) await Task.Delay(10, token);
        var assembly = typeof(GnaNotificationDbContext).Assembly;
        var subscribers = new[] { "NotificationMessageSubscriber", "NotificationDeliverySubscriber" }
            .Select(name => (IHostedService)ActivatorUtilities.CreateInstance(app.Services,
                assembly.GetType("GnaService.Infrastructure.Messaging." + name)!)).ToArray();
        foreach (var subscriber in subscribers) await subscriber.StartAsync(token);
        using var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<GnaNotificationDbContext>>(
            app.Services, TimeSpan.FromMilliseconds(500));
        await dispatcher.StartAsync(token);
        try
        {
            var messageId = Guid.NewGuid();
            var bus = app.Services.GetRequiredService<IMessageBus>();
            while (true)
            {
                try { await bus.PublishAsync(messageId, new NotificationCreatedMessage(student, "Rabbit to browser",
                        Guid.Parse("11111111-1111-1111-1111-111111111111"), 2,
                        Guid.Parse("22222222-2222-2222-2222-222222222222"), "FaqSuggested", "AwaitingConfirmation", NotificationEventType.FaqAnswered),
                    MessageRoutes.Notifications, MessageRoutes.NotificationCreated, token); break; }
                catch (Exception) when (!token.IsCancellationRequested) { await Task.Delay(100, token); }
            }
            var receive = socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            var received = await receive;
            using var frame = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count).TrimEnd('\u001e'));
            Assert.Equal("NotificationReceived", frame.RootElement.GetProperty("target").GetString());
            var payload = frame.RootElement.GetProperty("arguments")[0];
            Assert.Equal(messageId, payload.GetProperty("notificationId").GetGuid());
            Assert.Equal(student, payload.GetProperty("userId").GetString());
            Assert.Equal("FaqAnswered", payload.GetProperty("eventType").GetString());
            Assert.Equal(2, payload.GetProperty("generation").GetInt32());
            Assert.Equal("AwaitingConfirmation", payload.GetProperty("status").GetString());
            await using var scope = app.Services.CreateAsyncScope();
            Assert.True(await scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Notifications.AnyAsync(n => n.Id == messageId, token));
        }
        finally
        {
            await dispatcher.StopAsync(default);
            foreach (var subscriber in subscribers) { await subscriber.StopAsync(default); (subscriber as IDisposable)?.Dispose(); }
        }
    }

    public async Task DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
        if (Environment.GetEnvironmentVariable("BIFROST_TEST_POSTGRES") is not { } admin) return;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}

public sealed class GnaPostgresFactAttribute : FactAttribute
{
    public GnaPostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BIFROST_TEST_POSTGRES")))
            Skip = "Set BIFROST_TEST_POSTGRES to an isolated PostgreSQL test server.";
    }
}

public sealed class GnaBrokerFactAttribute : FactAttribute
{
    public GnaBrokerFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BIFROST_TEST_POSTGRES")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ")))
            Skip = "Set BIFROST_TEST_POSTGRES and BIFROST_TEST_RABBITMQ to isolated test servers.";
    }
}
