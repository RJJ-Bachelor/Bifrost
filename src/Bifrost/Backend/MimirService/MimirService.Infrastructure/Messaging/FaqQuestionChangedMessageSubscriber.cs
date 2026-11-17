using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimirService.Application.Services.Messaging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Infrastructure.Messaging;

internal sealed class FaqQuestionChangedMessageSubscriber(
    IMessageBus messageBus,
    IServiceScopeFactory scopeFactory,
    ILogger<FaqQuestionChangedMessageSubscriber> logger) : BackgroundService
{
    private const string QueueName = "mimir-faq-question-changed";
    private const string ExchangeName = MessageRoutes.Requests;
    private const string FaqQuestionChangedRoutingKey = MessageRoutes.FaqQuestionChanged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var subscription = await messageBus.SubscribeAsync<FaqQuestionChangedIntegrationEvent>(
            QueueName,
            ExchangeName,
            FaqQuestionChangedRoutingKey,
            HandleMessageAsync,
            stoppingToken);

        logger.LogInformation(
            "Mimir subscribed to {ExchangeName} with queue {QueueName} using {RoutingKey}.",
            ExchangeName,
            QueueName,
            FaqQuestionChangedRoutingKey);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleMessageAsync(
        MessageEnvelope<FaqQuestionChangedIntegrationEvent> message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var messageHandler = scope.ServiceProvider
            .GetRequiredService<IFaqQuestionChangedMessageHandler>();

        await messageHandler.HandleAsync(message, cancellationToken);
    }
}
