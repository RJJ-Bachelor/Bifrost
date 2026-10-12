using EirService.HelpRequests.Domain.Entities;
using Shared.Application.Messaging.Outbox;

namespace EirService.HelpRequests.Application.Repositories;

public interface IHelpRequestRepository
{
    Task AddAsync(HelpRequest request, IReadOnlyCollection<OutgoingMessage> messages, CancellationToken cancellationToken);
    Task<IReadOnlyList<HelpRequest>> GetByUserIdAsync(string userId, CancellationToken cancellationToken);
}
