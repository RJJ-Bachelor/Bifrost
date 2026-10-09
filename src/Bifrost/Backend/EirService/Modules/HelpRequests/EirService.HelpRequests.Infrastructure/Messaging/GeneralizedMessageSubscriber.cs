using EirService.HelpRequests.Application.Services.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace EirService.HelpRequests.Infrastructure.Messaging;

internal sealed class GeneralizedMessageSubscriber(
    IMessageBus messageBus,
    IServiceScopeFactory scopeFactory,
    ILogger<GeneralizedMessageSubscriber> logger) : BackgroundService
{
    private const string QueueName = "eir-generalized-created";
    private const string ExchangeName = "requests";
    private const string GeneralizedCreatedRoutingKey = "generalized.created";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var subscription = await messageBus.SubscribeAsync<GeneralizedCreatedMessage>(
            QueueName,
            ExchangeName,
            GeneralizedCreatedRoutingKey,
            HandleMessageAsync,
            stoppingToken);

        logger.LogInformation(
            "Eir subscribed to {ExchangeName} with queue {QueueName} using {RoutingKey}.",
            ExchangeName,
            QueueName,
            GeneralizedCreatedRoutingKey);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleMessageAsync(
        MessageEnvelope<GeneralizedCreatedMessage> message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var messageHandler = scope.ServiceProvider
            .GetRequiredService<IGeneralizedMessageHandler>();

        await messageHandler.HandleAsync(message, cancellationToken);
    }
}
