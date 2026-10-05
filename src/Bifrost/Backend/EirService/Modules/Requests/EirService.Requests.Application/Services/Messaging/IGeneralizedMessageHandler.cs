using Shared.Application.Messaging;

namespace EirService.Requests.Application.Services.Messaging;

public interface IGeneralizedMessageHandler
{
    Task HandleAsync(
        MessageEnvelope<GeneralizedCreatedMessage> message,
        CancellationToken cancellationToken);
}
