using System.Diagnostics;
using System.Text.Json;
using GnaService.Application.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace GnaService.Infrastructure.Persistence.Outbox;

public sealed class NotificationOutboxDispatcher(
    IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
    ILogger<NotificationOutboxDispatcher> logger, TimeSpan pollingInterval = default) : BackgroundService
{
    private const int PageSize = 50;
    private const int DispatchConcurrency = 8;
    private static readonly ActivitySource Activities = new("Bifrost.Messaging");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = pollingInterval == default ? TimeSpan.FromMilliseconds(500) : pollingInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // A full successful page may leave more work; only wait when idle or after failure.
                if (await DispatchPageAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification outbox processing failed; pending messages will be retried.");
            }
            try { await Task.Delay(interval, timeProvider, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    public Task DispatchPendingAsync(CancellationToken cancellationToken)
        => DispatchPageAsync(cancellationToken);

    private async Task<bool> DispatchPageAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
        var pending = await dbContext.Outbox
            .Where(message => message.PublishedAtUtc == null && message.RoutingKey == MessageRoutes.NotificationDeliveryRequested)
            .OrderBy(message => message.LastAttemptAtUtc ?? message.CreatedAtUtc).ThenBy(message => message.Id)
            .Take(PageSize)
            .Select(message => message.Id)
            .ToArrayAsync(cancellationToken);
        var processed = 0;
        await Parallel.ForEachAsync(pending, new ParallelOptions
        {
            MaxDegreeOfParallelism = DispatchConcurrency,
            CancellationToken = cancellationToken
        }, async (messageId, token) =>
        {
            if (await DispatchMessageAsync(messageId, token)) Interlocked.Increment(ref processed);
        });
        if (processed > 0)
            logger.LogInformation("Processed {MessageCount} notification outbox messages.", processed);
        return pending.Length == PageSize && processed == pending.Length;
    }

    private async Task<bool> DispatchMessageAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
        var message = await dbContext.Outbox.SingleOrDefaultAsync(
            item => item.Id == messageId && item.PublishedAtUtc == null, cancellationToken);
        if (message is null) return false;

        var previousActivity = Activity.Current;
        Activity.Current = null;
        try
        {
            ActivityContext.TryParse(message.TraceParent, message.TraceState, isRemote: true, out var parentContext);
            using var activity = Activities.StartActivity("deliver notification outbox", ActivityKind.Internal, parentContext);
            activity?.SetTag("messaging.message.id", message.MessageId);
            var notification = JsonSerializer.Deserialize<NotificationDeliveryRequestedMessage>(message.Payload, JsonOptions)
                ?? throw new JsonException("The notification delivery payload is missing.");
            await dispatcher.DispatchAsync(notification, cancellationToken);
            message.MarkPublished(timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            message.MarkFailed(timeProvider.GetUtcNow());
            logger.LogError(exception, "Notification outbox message {MessageId} failed; it will be retried.", message.MessageId);
            await dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }
        finally { Activity.Current = previousActivity; }
    }
}
