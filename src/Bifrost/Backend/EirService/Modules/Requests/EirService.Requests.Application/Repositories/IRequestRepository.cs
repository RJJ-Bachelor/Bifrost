using EirService.Requests.Domain.Entities;

namespace EirService.Requests.Application.Repositories;

public interface IRequestRepository
{
    Task AddAsync(EirRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<EirRequest>> GetByUserIdAsync(string userId, CancellationToken cancellationToken);
}
