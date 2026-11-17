using System.Diagnostics;
using System.Text.Json;
using Shared.Application.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Messaging;

namespace Shared.Infrastructure.Persistence.Outbox;

/// <summary>Delivers module messages through their configured transport and drains legacy command routes.</summary>
public sealed class InternalOutboxDispatcher<TDbContext>(
    IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
    ILogger<InternalOutboxDispatcher<TDbContext>> logger, TimeSpan pollingInterval = default)
    : BackgroundService where TDbContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            pollingInterval == default ? TimeSpan.FromMilliseconds(500) : pollingInterval, timeProvider);
        do
        {
            try { await DispatchPendingAsync(stoppingToken, false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Internal outbox delivery failed; pending commands will be retried."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task DispatchPendingAsync(CancellationToken cancellationToken, bool ignoreBackoff = true)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var pending = await dbContext.Set<OutboxMessage>()
            .Where(message => message.PublishedAtUtc == null && message.ExchangeName == InternalMessageRoutes.Exchange && message.DeadLetteredAtUtc == null &&
                (ignoreBackoff || message.NextAttemptAtUtc == null || message.NextAttemptAtUtc <= timeProvider.GetUtcNow()))
            .OrderBy(message => message.LastAttemptAtUtc ?? message.CreatedAtUtc).ThenBy(message => message.Id)
            .Take(50).ToListAsync(cancellationToken);
        foreach (var message in pending)
        {
            var previousActivity = Activity.Current;
            Activity.Current = null;
            try
            {
                using var activity = MessageBusTelemetry.StartOutboxActivity(message.TraceParent, message.TraceState);
                // Isolate failed receiving contexts from later deliveries.
                await using var receiver = scopeFactory.CreateAsyncScope();
                var envelope = new MessageEnvelope<JsonElement>(message.MessageId,
                    JsonSerializer.Deserialize<JsonElement>(message.Payload), message.Metadata);
                var module = receiver.ServiceProvider.GetServices<ModuleMessageRegistration>()
                    .SingleOrDefault(candidate => candidate.Route == message.RoutingKey);
                if (module is not null)
                    await module.DispatchAsync(receiver.ServiceProvider, envelope, cancellationToken);
                else
                {
                    var registration = receiver.ServiceProvider.GetServices<InternalCommandRegistration>()
                        .SingleOrDefault(candidate => candidate.RoutingKey == message.RoutingKey)
                        ?? throw new InvalidDataException($"No internal handler is registered for '{message.RoutingKey}'.");
                    if (registration.CommandType.FullName != message.MessageType)
                        throw new InvalidDataException($"The command type does not match internal route '{message.RoutingKey}'.");
                    await registration.DispatchAsync(receiver.ServiceProvider, envelope, cancellationToken);
                }
                message.MarkPublished(timeProvider.GetUtcNow());
                // A crash after the receiving commit can redeliver the same command; handlers must be idempotent.
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                message.ScheduleRetry(timeProvider.GetUtcNow(), exception, exception is InvalidDataException or JsonException);
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogError(exception, "Internal command {MessageId} to {RoutingKey} failed; parked: {Parked}.",
                    message.MessageId, message.RoutingKey, message.DeadLetteredAtUtc is not null);
            }
            finally { Activity.Current = previousActivity; }
        }
    }
}
