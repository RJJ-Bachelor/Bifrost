using EirService.Contracts.HelpRequests.HandleSessionChange;
using FastEndpoints;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace EirService.HelpRequests.Application.Features.Internal.HandleSessionChange;

public sealed class SessionUpdatedMessageHandler(ICommandHandler<HandleSessionChangeCommand> commands)
    : IMessageHandler<SessionUpdatedIntegrationEvent>
{
    public Task HandleAsync(MessageEnvelope<SessionUpdatedIntegrationEvent> message, CancellationToken cancellationToken)
    {
        var update = message.Payload;
        if (update.EventId == Guid.Empty || update.SessionId == Guid.Empty || update.Generation < 1 ||
            !Enum.IsDefined(update.EventType) || update.EventType != SessionUpdateType.Deleted &&
            (update.PreviousGeneration is not { } previous || previous < 1 || previous != update.Generation - 1))
            throw new MessageRejectedException("The session update has invalid identifiers, generation or change type.");
        return commands.ExecuteAsync(new(update.EventId, update.SessionId, update.Generation,
            (SessionChangeType)update.EventType, update.UpdatedAtUtc, update.PreviousGeneration), cancellationToken);
    }
}
