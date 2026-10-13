using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Application.Services.Messaging;

public sealed class SessionUpdatedMessageHandler(ILogger<SessionUpdatedMessageHandler> logger) : ISessionUpdatedMessageHandler
{
    public Task HandleAsync(MessageEnvelope<SessionUpdatedIntegrationEvent> message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var update = message.Payload;
        switch (update.EventType)
        {
            case SessionUpdateType.Cleared:
                logger.LogInformation("Mimir received Cleared for session {SessionId}: generation {PreviousGeneration} -> {Generation}, event {EventId} at {UpdatedAtUtc}",
                    update.SessionId, update.PreviousGeneration, update.Generation, update.EventId, update.UpdatedAtUtc);
                break;
            case SessionUpdateType.Deleted:
                logger.LogInformation("Mimir received Deleted for session {SessionId} at generation {Generation}, event {EventId} at {UpdatedAtUtc}",
                    update.SessionId, update.Generation, update.EventId, update.UpdatedAtUtc);
                break;
            case SessionUpdateType.Archived:
                logger.LogInformation("Mimir received Archived for session {SessionId} at generation {Generation}, event {EventId} at {UpdatedAtUtc}",
                    update.SessionId, update.Generation, update.EventId, update.UpdatedAtUtc);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(update.EventType), update.EventType, "Unknown session update type.");
        }
        return Task.CompletedTask;
    }
}
