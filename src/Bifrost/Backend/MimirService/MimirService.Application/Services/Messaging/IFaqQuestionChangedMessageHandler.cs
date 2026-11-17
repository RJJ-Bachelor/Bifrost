using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Application.Services.Messaging;

public interface IFaqQuestionChangedMessageHandler
{
    Task HandleAsync(MessageEnvelope<FaqQuestionChangedIntegrationEvent> message, CancellationToken cancellationToken);
}
