using MimirService.Domain.Entities;

namespace MimirService.Application.Repositories;

public interface IMimirRequestRepository
{
    Task AddAsync(
        MimirRequest request,
        CancellationToken cancellationToken);
}
