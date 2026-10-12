using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace EirService.HelpRequests.Application.Features.Internal.SessionUpdated;

public sealed class SessionUpdatedMessageHandler(ILogger<SessionUpdatedMessageHandler> logger) : ISessionUpdatedMessageHandler
{
    public Task HandleAsync(MessageEnvelope<SessionUpdatedIntegrationEvent> message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var update = message.Payload;
        if (update.EventType == SessionUpdateType.Deleted)
        {
            // Cleanup will be implemented here for all requests in the session, across every generation.
            logger.LogInformation("HelpRequests received deletion of session {SessionId}: cleanup requested for all session messages across all generations. Event {EventId} at {UpdatedAtUtc}. Cleanup is not implemented yet.",
                update.SessionId, update.EventId, update.UpdatedAtUtc);
        }
        return Task.CompletedTask;
    }
}
