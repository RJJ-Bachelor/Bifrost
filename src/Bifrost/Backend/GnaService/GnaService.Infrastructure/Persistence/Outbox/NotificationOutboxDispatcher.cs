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
    private static readonly ActivitySource Activities = new("Bifrost.Messaging");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            pollingInterval == default ? TimeSpan.FromMilliseconds(500) : pollingInterval, timeProvider);
        do
        {
            try { await DispatchPendingAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification outbox processing failed; pending messages will be retried.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task DispatchPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
        var pending = await dbContext.Outbox
            .Where(message => message.PublishedAtUtc == null && message.RoutingKey == MessageRoutes.NotificationDeliveryRequested)
            .OrderBy(message => message.LastAttemptAtUtc ?? message.CreatedAtUtc).ThenBy(message => message.Id)
            .Take(50).ToListAsync(cancellationToken);
        var processed = 0;
        foreach (var message in pending)
        {
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
                // A completed dispatch attempt is terminal, including an unavailable delivery channel.
                message.MarkPublished(timeProvider.GetUtcNow());
                await dbContext.SaveChangesAsync(cancellationToken);
                processed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                // Failed rows move behind unattempted work, matching the shared outbox dispatcher.
                message.MarkFailed(timeProvider.GetUtcNow());
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogError(exception, "Notification outbox message {MessageId} failed; it will be retried.", message.MessageId);
            }
            finally { Activity.Current = previousActivity; }
        }
        if (processed > 0)
            logger.LogInformation("Processed {MessageCount} notification outbox messages.", processed);
    }
}
