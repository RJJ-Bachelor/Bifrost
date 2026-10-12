using EirService.HelpRequests.Application.Features.Internal.SessionUpdated;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace EirService.HelpRequests.Infrastructure.Messaging;

internal sealed class SessionUpdatedMessageSubscriber(
    IMessageBus messageBus,
    IServiceScopeFactory scopeFactory,
    ILogger<SessionUpdatedMessageSubscriber> logger) : IHostedService, IAsyncDisposable
{
    private const string QueueName = "eir-helprequests-session-updated";
    private IAsyncDisposable? subscription;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Bind the queue before startup continues to the Sessions outbox dispatcher.
        subscription = await messageBus.SubscribeAsync<SessionUpdatedIntegrationEvent>(
            QueueName, MessageRoutes.Sessions, MessageRoutes.SessionUpdated, HandleMessageAsync, cancellationToken);
        logger.LogInformation("HelpRequests subscribed to {ExchangeName} with queue {QueueName} using {RoutingKey}.",
            MessageRoutes.Sessions, QueueName, MessageRoutes.SessionUpdated);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (subscription is { } current)
        {
            subscription = null;
            await current.DisposeAsync();
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    private async Task HandleMessageAsync(MessageEnvelope<SessionUpdatedIntegrationEvent> message, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ISessionUpdatedMessageHandler>();
        await handler.HandleAsync(message, cancellationToken);
    }
}
