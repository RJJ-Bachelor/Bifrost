using Shared.Application.Messaging;

namespace EirService.Requests.Application.Messaging;

public interface IGeneralizedMessageHandler
{
    Task HandleAsync(
        MessageEnvelope<GeneralizedCreatedMessage> message,
        CancellationToken cancellationToken);
}
