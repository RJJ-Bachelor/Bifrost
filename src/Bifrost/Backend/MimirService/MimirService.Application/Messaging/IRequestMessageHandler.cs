using Shared.Application.Messaging;

namespace MimirService.Application.Messaging;

public interface IRequestMessageHandler
{
    Task HandleAsync(
        MessageEnvelope<RequestCreatedMessage> message,
        CancellationToken cancellationToken);
}
