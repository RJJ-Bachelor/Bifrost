using MimirService.Application.Repositories;
using MimirService.Domain.Entities;
using MimirService.Infrastructure.Persistence;

namespace MimirService.Infrastructure.Repositories;

public sealed class MimirRequestRepository(
    MimirRequestDbContext dbContext) : IMimirRequestRepository
{
    public async Task AddAsync(
        MimirRequest request,
        CancellationToken cancellationToken)
    {
        await dbContext.Requests.AddAsync(request, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
