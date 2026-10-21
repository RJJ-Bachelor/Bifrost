using MimirService.Domain.Entities;
using Shared.Application.Messaging.Outbox;

namespace MimirService.Application.Repositories;

public interface IHelpRequestRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> TryAddAsync(HelpRequest request, OutgoingMessage? reply, CancellationToken cancellationToken);
}
