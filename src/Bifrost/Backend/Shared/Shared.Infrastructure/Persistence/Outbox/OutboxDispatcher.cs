using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Messaging;

namespace Shared.Infrastructure.Persistence.Outbox;

public sealed class OutboxDispatcher<TDbContext>(
    IServiceScopeFactory scopeFactory, IMessageBus messageBus, TimeProvider timeProvider,
    ILogger<OutboxDispatcher<TDbContext>> logger, TimeSpan pollingInterval = default) : BackgroundService where TDbContext : DbContext
{
    private const int PageSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = pollingInterval == default ? TimeSpan.FromSeconds(5) : pollingInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // A full successful page may leave more work; only wait when idle or after failure.
                if (await PublishPageAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox delivery failed; pending messages will be retried.");
            }
            // Delay from the completed attempt: failures must not spin on an already elapsed timer.
            try { await Task.Delay(interval, timeProvider, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    public Task PublishPendingAsync(CancellationToken cancellationToken)
        => PublishPageAsync(cancellationToken);

    private async Task<bool> PublishPageAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var pending = await dbContext.Set<OutboxMessage>().Where(message => message.PublishedAtUtc == null &&
                message.ExchangeName != InternalMessageRoutes.Exchange)
            .OrderBy(message => message.LastAttemptAtUtc ?? message.CreatedAtUtc).ThenBy(message => message.Id)
            .Take(PageSize).ToListAsync(cancellationToken);
        var published = 0;
        foreach (var message in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previousActivity = Activity.Current;
            Activity.Current = null;
            try
            {
                using var activity = MessageBusTelemetry.StartOutboxActivity(message.TraceParent, message.TraceState);
                var payload = JsonSerializer.Deserialize<JsonElement>(message.Payload);
                await messageBus.PublishAsync(message.MessageId, payload, message.ExchangeName, message.RoutingKey,
                    cancellationToken, message.MessageType);
                message.MarkPublished(timeProvider.GetUtcNow());
                published++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                // Failed rows move behind unattempted work; one bad route cannot starve the queue.
                message.MarkFailed(timeProvider.GetUtcNow());
                logger.LogError(exception, "Outbox message {MessageId} to {RoutingKey} failed; it will be retried.",
                    message.MessageId, message.RoutingKey);
            }
            finally { Activity.Current = previousActivity; }

            // Persist each outcome before the next message. A crash can repeat its stable MessageId.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        if (published > 0)
            logger.LogInformation("Published {MessageCount} outbox messages from {DbContext}.", published, typeof(TDbContext).Name);
        return pending.Count == PageSize && published == pending.Count;
    }
}
