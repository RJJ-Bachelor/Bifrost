using EirService.Contracts.HelpRequests.HandleSessionChange;
using EirService.HelpRequests.Application.Repositories;
using FastEndpoints;
using Microsoft.Extensions.Logging;

namespace EirService.HelpRequests.Application.Features.Internal.HandleSessionChange;

public sealed class HandleSessionChangeCommandHandler(
    IArchivedConversationRepository repository,
    ILogger<HandleSessionChangeCommandHandler> logger) : ICommandHandler<HandleSessionChangeCommand>
{
    public async Task ExecuteAsync(HandleSessionChangeCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command.EventType == SessionChangeType.Deleted)
        {
            // Cleanup will be implemented here for all requests in the session, across every generation.
            logger.LogInformation("HelpRequests received deletion of session {SessionId}: cleanup requested for all session messages across all generations. Event {EventId} at {UpdatedAtUtc}. Cleanup is not implemented yet.",
                command.SessionId, command.EventId, command.UpdatedAtUtc);
        }
        if (command.EventType == SessionChangeType.Archived)
        {
            if (command.SessionId == Guid.Empty || command.PreviousGeneration is not { } generation || generation < 1 || generation != command.Generation - 1)
                throw new ArgumentException("An archive event requires a preceding generation.", nameof(command));
            await repository.ArchiveQueueAsync(command.SessionId, generation, cancellationToken);
        }
    }
}
