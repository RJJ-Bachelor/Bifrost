using GnaService.Application.Services.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace GnaService.Infrastructure.Messaging;

internal sealed class NotificationMessageSubscriber(
    IMessageBus messageBus,
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationMessageSubscriber> logger) : BackgroundService
{
    private const string QueueName = "gna-notification-requested";
    private const string ExchangeName = "notifications";
    private const string NotificationRequestedRoutingKey = "gna-notification-requested";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var subscription = await messageBus.SubscribeAsync<NotificationCreatedMessage>(
            QueueName,
            ExchangeName,
            NotificationRequestedRoutingKey,
            HandleMessageAsync,
            stoppingToken);

        logger.LogInformation(
            "Gna subscribed to {ExchangeName} with queue {QueueName} using {RoutingKey}.",
            ExchangeName,
            QueueName,
            NotificationRequestedRoutingKey);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleMessageAsync(
        MessageEnvelope<NotificationCreatedMessage> message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var messageHandler = scope.ServiceProvider
            .GetRequiredService<INotificationMessageHandler>();

        await messageHandler.HandleAsync(message, cancellationToken);
    }
}
