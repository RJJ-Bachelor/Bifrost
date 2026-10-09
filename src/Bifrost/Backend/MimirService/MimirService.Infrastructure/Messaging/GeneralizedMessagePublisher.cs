using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Infrastructure.Messaging;

internal sealed class GeneralizedMessagePublisher(IMessageBus messageBus)
    : IGeneralizedMessagePublisher
{
    private const string ExchangeName = "requests";
    private const string GeneralizedCreatedRoutingKey = "generalized.created";

    public Task PublishGeneralizedCreatedAsync(
        string id,
        string message,
        CancellationToken cancellationToken = default)
    {
        return messageBus.PublishAsync(
            id,
            new GeneralizedCreatedMessage(message),
            ExchangeName,
            GeneralizedCreatedRoutingKey,
            cancellationToken);
    }
}
