using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using MimirService.Application.Features.Internal.SessionUpdated;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Infrastructure.Messaging;

internal sealed class SessionUpdatedMessageSubscriber(
    IMessageBus messageBus,
    IServiceScopeFactory scopeFactory,
    ILogger<SessionUpdatedMessageSubscriber> logger) : BackgroundService
{
    private const string QueueName = "mimir-session-updated";
    private const string ExchangeName = MessageRoutes.Sessions;
    private const string SessionUpdatedRoutingKey = MessageRoutes.SessionUpdated;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var subscription = await messageBus.SubscribeAsync<SessionUpdatedIntegrationEvent>(
            QueueName,
            ExchangeName,
            SessionUpdatedRoutingKey,
            HandleMessageAsync,
            stoppingToken);

        logger.LogInformation(
            "Mimir subscribed to {ExchangeName} with queue {QueueName} using {RoutingKey}.",
            ExchangeName,
            QueueName,
            SessionUpdatedRoutingKey);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleMessageAsync(
        MessageEnvelope<SessionUpdatedIntegrationEvent> message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var messageHandler = scope.ServiceProvider
            .GetRequiredService<ISessionUpdatedMessageHandler>();

        await messageHandler.HandleAsync(message, cancellationToken);
    }
}
