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
        foreach (var registration in builder.Services.Where(service => service.ServiceType == typeof(IHostedService)).ToArray())
            if (registration.ImplementationType?.Namespace?.StartsWith("GnaService.") == true ||
                registration.ImplementationType?.Namespace?.StartsWith("Shared.Infrastructure.") == true ||
                registration.ImplementationFactory is not null)
                builder.Services.Remove(registration);
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
        var incoming = new MessageEnvelope<NotificationCreatedMessage>(Guid.NewGuid(), new("student", "Hello"));
        await handler.HandleAsync(incoming, default);
        await handler.HandleAsync(incoming, default);
        var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
        Assert.Single(await db.Notifications.ToArrayAsync());
        var outbox = Assert.Single(await db.Outbox.ToArrayAsync());
        Assert.Equal(incoming.Id, outbox.MessageId);
        Assert.Equal(MessageRoutes.NotificationDeliveryRequested, outbox.RoutingKey);
        Assert.Null(outbox.PublishedAtUtc);
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
                try { await bus.PublishAsync(messageId, new NotificationCreatedMessage(student, "Rabbit to browser"),
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
