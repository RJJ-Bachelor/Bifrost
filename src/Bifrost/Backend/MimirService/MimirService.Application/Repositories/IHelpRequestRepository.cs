using MimirService.Domain.Entities;
using Shared.Application.Messaging.Outbox;

namespace MimirService.Application.Repositories;

public interface IHelpRequestRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    // The returned requests are tracked, so linking one to a generalization is saved by TryAddAsync.
    Task<IReadOnlyList<HelpRequest>> GetWaitingAsync(Guid sessionId, int generation, CancellationToken cancellationToken);

    Task<bool> TryAddAsync(HelpRequest request, Generalization? generalization, OutgoingMessage? reply,
        CancellationToken cancellationToken);
}
