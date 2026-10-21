using EirService.KnowledgeBase.Application.Features.Internal.FaqAnswered;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace EirService.KnowledgeBase.Infrastructure.Messaging;

internal sealed class FaqAnsweredMessageSubscriber(IMessageBus messageBus, IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var subscription = await messageBus.SubscribeAsync<FaqAnsweredMessage>(
            "eir-faq-answered", MessageRoutes.Requests, MessageRoutes.FaqAnswered, async (message, cancellationToken) =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<FaqAnsweredMessageHandler>().HandleAsync(message.Payload, cancellationToken);
            }, stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
