using GnaService.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace GnaService.Infrastructure.Messaging;

// Generic refreshes are realtime-only. RabbitMQ retains the delivery until SignalR accepts it.
internal sealed class SessionChangedSubscriber(IMessageBus messageBus, IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var subscription = await messageBus.SubscribeAsync<SessionChangedMessage>(
            MessageRoutes.SessionChanged, MessageRoutes.Notifications, MessageRoutes.SessionChanged,
            SendAsync, stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task SendAsync(MessageEnvelope<SessionChangedMessage> message, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IRealtimeSessionChangeSender>()
            .SendAsync(message.Id, message.Payload, cancellationToken);
    }
}
