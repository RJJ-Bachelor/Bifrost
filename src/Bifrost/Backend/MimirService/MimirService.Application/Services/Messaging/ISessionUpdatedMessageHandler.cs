using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Application.Services.Messaging;

public interface ISessionUpdatedMessageHandler
{
    Task HandleAsync(MessageEnvelope<SessionUpdatedIntegrationEvent> message, CancellationToken cancellationToken);
}
