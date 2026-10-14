using GnaService.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace GnaService.Infrastructure.Messaging;

internal sealed class NotificationDeliverySubscriber(IMessageBus messageBus, IServiceScopeFactory scopeFactory,
    ILogger<NotificationDeliverySubscriber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var subscription = await messageBus.SubscribeAsync<NotificationDeliveryRequestedMessage>(
            "gna-notification-delivery", MessageRoutes.Notifications, MessageRoutes.NotificationDeliveryRequested,
            async (message, token) =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchAsync(message.Payload, token);
            }, stoppingToken);
        logger.LogInformation("Gna subscribed to notification delivery requests.");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
