using EirService.HelpRequests.Domain.Entities;
using Shared.Application.Messaging.Outbox;

namespace EirService.HelpRequests.Application.Repositories;

public interface IRequestRepository
{
    Task AddAsync(EirRequest request, IReadOnlyCollection<OutgoingMessage> messages, CancellationToken cancellationToken);
    Task<IReadOnlyList<EirRequest>> GetByUserIdAsync(string userId, CancellationToken cancellationToken);
}
