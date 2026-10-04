using EirService.Requests.Application.Repositories;
using EirService.Requests.Domain.Entities;
using EirService.Requests.Infrastructure.Persistence;

namespace EirService.Requests.Infrastructure.Repositories;

public sealed class RequestRepository(EirRequestDbContext dbContext) : IRequestRepository
{
    public async Task AddAsync(EirRequest request, CancellationToken cancellationToken)
    {
        await dbContext.Requests.AddAsync(request, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
