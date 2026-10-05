using Shared.Application.Messaging;

namespace MimirService.Application.Services.Messaging;

public interface IRequestMessageHandler
{
    Task HandleAsync(
        MessageEnvelope<RequestCreatedMessage> message,
        CancellationToken cancellationToken);
}
