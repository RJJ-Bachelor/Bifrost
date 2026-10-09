using EirService.HelpRequests.Domain.Entities;

namespace EirService.HelpRequests.Application.Repositories;

public interface IRequestRepository
{
    Task AddAsync(EirRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<EirRequest>> GetByUserIdAsync(string userId, CancellationToken cancellationToken);
}
