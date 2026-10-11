using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Application.Services.Messaging;

public interface IHelpRequestMessageHandler
{
    Task HandleAsync(
        MessageEnvelope<HelpRequestCreatedMessage> message,
        CancellationToken cancellationToken);
}
