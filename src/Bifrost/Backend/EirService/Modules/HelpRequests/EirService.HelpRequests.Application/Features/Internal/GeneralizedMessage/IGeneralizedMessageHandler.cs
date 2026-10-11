using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace EirService.HelpRequests.Application.Features.Internal.GeneralizedMessage;

public interface IGeneralizedMessageHandler
{
    Task HandleAsync(
        MessageEnvelope<GeneralizedCreatedMessage> message,
        CancellationToken cancellationToken);
}
