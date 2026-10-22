using EirService.HelpRequests.Infrastructure.Persistence;
using GnaService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimirService.Infrastructure.Persistence;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.Test;

public sealed partial class OutboxIntegrationTests
{
    [PostgresFact]
    public Task EirOutboxOnlyLogsWhenMessagesArePublished() =>
        AssertOutboxLogsChangesAsync<EirRequestDbContext>("eir");

    [PostgresFact]
    public Task MimirOutboxOnlyLogsWhenMessagesArePublished() =>
        AssertOutboxLogsChangesAsync<MimirRequestDbContext>("mimir");

    [PostgresFact]
    public Task GnaOutboxOnlyLogsWhenMessagesArePublished() =>
        AssertOutboxLogsChangesAsync<GnaNotificationDbContext>("gna");

    private async Task AssertOutboxLogsChangesAsync<TDbContext>(string service) where TDbContext : DbContext
    {
        await using var services = BuildServices(service);
        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TDbContext>().Database.MigrateAsync();
        var logger = new OutboxLog<TDbContext>();
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<TDbContext>>(services, logger);

        await dispatcher.PublishPendingAsync(CancellationToken.None);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Empty(logger.Entries);

        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
            db.Set<OutboxMessage>().AddRange(Enumerable.Range(1, 2).Select(number =>
                OutboxMessage.Create(new OutgoingMessage(Guid.NewGuid(), new { Number = number },
                    "test-exchange", "test-change"), DateTimeOffset.UtcNow)));
            await db.SaveChangesAsync();
        }

        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Equal(2, bus.Messages.Count);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("Published 2 outbox messages", entry.Message);
        Assert.Contains(typeof(TDbContext).Name, entry.Message);
        await using (var scope = services.CreateAsyncScope())
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<TDbContext>().Set<OutboxMessage>()
                .CountAsync(message => message.PublishedAtUtc != null));

        await dispatcher.PublishPendingAsync(CancellationToken.None);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Single(logger.Entries);
    }

    private sealed class OutboxLog<TDbContext> : ILogger<OutboxDispatcher<TDbContext>> where TDbContext : DbContext
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
