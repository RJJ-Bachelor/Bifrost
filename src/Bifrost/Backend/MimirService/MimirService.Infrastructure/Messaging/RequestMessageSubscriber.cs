using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using MimirService.Application.Services.Messaging;

namespace MimirService.Infrastructure.Messaging;

internal sealed class RequestMessageSubscriber(
    IMessageBus messageBus,
    IServiceScopeFactory scopeFactory,
    ILogger<RequestMessageSubscriber> logger) : BackgroundService
{
    private const string QueueName = "mimir-request-created";
    private const string ExchangeName = "requests";
    private const string RequestCreatedRoutingKey = "request.created";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var subscription = await messageBus.SubscribeAsync<RequestCreatedMessage>(
            QueueName,
            ExchangeName,
            RequestCreatedRoutingKey,
            HandleMessageAsync,
            stoppingToken);

        logger.LogInformation(
            "Mimir subscribed to {ExchangeName} with queue {QueueName} using {RoutingKey}.",
            ExchangeName,
            QueueName,
            RequestCreatedRoutingKey);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleMessageAsync(
        MessageEnvelope<RequestCreatedMessage> message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var messageHandler = scope.ServiceProvider
            .GetRequiredService<IRequestMessageHandler>();

        await messageHandler.HandleAsync(message, cancellationToken);
    }
}
