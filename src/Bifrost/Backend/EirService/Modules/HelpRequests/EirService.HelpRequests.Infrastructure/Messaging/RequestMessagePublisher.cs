using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace EirService.HelpRequests.Infrastructure.Messaging;

internal sealed class RequestMessagePublisher(IMessageBus messageBus)
    : IRequestMessagePublisher
{
    private const string ExchangeName = "requests";
    private const string RequestCreatedRoutingKey = "request.created";

    public Task PublishRequestCreatedAsync(
        string id,
        string message,
        CancellationToken cancellationToken = default)
    {
        return messageBus.PublishAsync(
            id,
            new RequestCreatedMessage(message),
            ExchangeName,
            RequestCreatedRoutingKey,
            cancellationToken);
    }
}
