using EirService.KnowledgeBase.Application.Features.Internal.RegisterGeneralization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace EirService.KnowledgeBase.Infrastructure.Messaging;

internal sealed class RegisterGeneralizationMessageSubscriber(
    IMessageBus messageBus,
    IServiceScopeFactory scopeFactory,
    ILogger<RegisterGeneralizationMessageSubscriber> logger) : BackgroundService
{
    private const string QueueName = "eir-generalized-created";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var subscription = await messageBus.SubscribeAsync<GeneralizedCreatedMessage>(
            QueueName, MessageRoutes.Requests, MessageRoutes.GeneralizedCreated, HandleMessageAsync, stoppingToken);

        logger.LogInformation("KnowledgeBase subscribed to {ExchangeName} with queue {QueueName} using {RoutingKey}.",
            MessageRoutes.Requests, QueueName, MessageRoutes.GeneralizedCreated);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleMessageAsync(MessageEnvelope<GeneralizedCreatedMessage> message, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<RegisterGeneralizationMessageHandler>();
        await handler.HandleAsync(message, cancellationToken);
    }
}
