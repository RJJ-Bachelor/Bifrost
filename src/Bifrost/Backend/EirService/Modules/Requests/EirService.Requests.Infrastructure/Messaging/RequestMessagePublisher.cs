using EirService.Requests.Application.Messaging;
using Shared.Infrastructure.Persistence.Messaging;

namespace EirService.Requests.Infrastructure.Messaging;

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
            message,
            ExchangeName,
            RequestCreatedRoutingKey,
            cancellationToken);
    }
}
