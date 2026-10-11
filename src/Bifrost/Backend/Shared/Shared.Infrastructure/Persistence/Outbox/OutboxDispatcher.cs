using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using Shared.Infrastructure.Persistence.Messaging;

namespace Shared.Infrastructure.Persistence.Outbox;

public sealed class OutboxDispatcher<TDbContext>(
    IServiceScopeFactory scopeFactory, IMessageBus messageBus, TimeProvider timeProvider,
    ILogger<OutboxDispatcher<TDbContext>> logger) : BackgroundService where TDbContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), timeProvider);
        do
        {
            try { await PublishPendingAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Outbox delivery failed; pending messages will be retried."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task PublishPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var pending = await dbContext.Set<OutboxMessage>().Where(message => message.PublishedAtUtc == null)
            .OrderBy(message => message.LastAttemptAtUtc ?? message.CreatedAtUtc).ThenBy(message => message.Id).Take(50).ToListAsync(cancellationToken);
        foreach (var message in pending)
        {
            var previousActivity = Activity.Current;
            Activity.Current = null;
            try
            {
                using var activity = MessageBusTelemetry.StartOutboxActivity(message.TraceParent, message.TraceState);
                var payload = JsonSerializer.Deserialize<JsonElement>(message.Payload);
                await messageBus.PublishAsync(message.MessageId, payload, message.ExchangeName, message.RoutingKey,
                    cancellationToken, message.MessageType);
                message.MarkPublished(timeProvider.GetUtcNow());
                // A crash after publish can deliver the same MessageId again. Consumers must be idempotent.
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                // Failed messages move behind unattempted messages so a failed route cannot starve the next batch.
                message.MarkFailed(timeProvider.GetUtcNow());
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogError(exception, "Outbox message {MessageId} to {RoutingKey} failed; it will be retried.",
                    message.MessageId, message.RoutingKey);
            }
            finally { Activity.Current = previousActivity; }
        }
    }
}
