using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EirService.HelpRequests.Infrastructure.Repositories;

public sealed class RequestRepository(EirRequestDbContext dbContext) : IRequestRepository
{
    public async Task AddAsync(EirRequest request, CancellationToken cancellationToken)
    {
        await dbContext.Requests.AddAsync(request, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EirRequest>> GetByUserIdAsync(string userId, CancellationToken cancellationToken)
    {
        return await dbContext.Requests.AsNoTracking()
            .Where(request => request.UserId == userId)
            .OrderBy(request => request.Id)
            .ToListAsync(cancellationToken);
    }
}
