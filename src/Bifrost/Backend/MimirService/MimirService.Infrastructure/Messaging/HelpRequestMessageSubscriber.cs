using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using MimirService.Application.Services.Messaging;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Infrastructure.Messaging;

internal sealed class HelpRequestMessageSubscriber(
    IMessageBus messageBus,
    IServiceScopeFactory scopeFactory,
    ILogger<HelpRequestMessageSubscriber> logger) : BackgroundService
{
    private const string QueueName = "mimir-request-created";
    private const string ExchangeName = MessageRoutes.Requests;
    private const string HelpRequestCreatedRoutingKey = MessageRoutes.HelpRequestCreated;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var subscription = await messageBus.SubscribeAsync<HelpRequestCreatedMessage>(
            QueueName,
            ExchangeName,
            HelpRequestCreatedRoutingKey,
            HandleMessageAsync,
            stoppingToken);

        logger.LogInformation(
            "Mimir subscribed to {ExchangeName} with queue {QueueName} using {RoutingKey}.",
            ExchangeName,
            QueueName,
            HelpRequestCreatedRoutingKey);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleMessageAsync(
        MessageEnvelope<HelpRequestCreatedMessage> message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var messageHandler = scope.ServiceProvider
            .GetRequiredService<IHelpRequestMessageHandler>();

        await messageHandler.HandleAsync(message, cancellationToken);
    }
}
