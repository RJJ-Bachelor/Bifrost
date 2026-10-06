using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Application.Services.Messaging;

public interface IRequestMessageHandler
{
    Task HandleAsync(
        MessageEnvelope<RequestCreatedMessage> message,
        CancellationToken cancellationToken);
}
