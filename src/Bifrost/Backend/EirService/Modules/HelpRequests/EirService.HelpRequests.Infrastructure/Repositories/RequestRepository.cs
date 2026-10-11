using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.HelpRequests.Infrastructure.Repositories;

public sealed class RequestRepository(EirRequestDbContext dbContext, TimeProvider timeProvider) : IRequestRepository
{
    public async Task AddAsync(EirRequest request, IReadOnlyCollection<OutgoingMessage> messages, CancellationToken cancellationToken)
    {
        await dbContext.Requests.AddAsync(request, cancellationToken);
        var now = timeProvider.GetUtcNow();
        dbContext.Outbox.AddRange(messages.Select(message => OutboxMessage.Create(message, now)));
        // Save the request and all outgoing messages in one transaction.
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
