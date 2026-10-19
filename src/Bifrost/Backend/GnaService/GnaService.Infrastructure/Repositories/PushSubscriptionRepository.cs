using GnaService.Application.Repositories;
using GnaService.Domain.Entities;
using GnaService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GnaService.Infrastructure.Repositories;

public sealed class PushSubscriptionRepository(GnaNotificationDbContext dbContext) : IPushSubscriptionRepository
{
    public async Task<IReadOnlyList<PushSubscription>> GetForUserAsync(string userId, CancellationToken cancellationToken) =>
        await dbContext.PushSubscriptions.AsNoTracking().Where(subscription => subscription.UserId ==
            Domain.ValueObjects.UserId.Create(userId)).ToListAsync(cancellationToken);

    public async Task SaveAsync(PushSubscription subscription, CancellationToken cancellationToken)
    {
        var existing = await dbContext.PushSubscriptions.SingleOrDefaultAsync(saved =>
            saved.UserId == subscription.UserId && saved.Endpoint == subscription.Endpoint, cancellationToken);
        if (existing is null) dbContext.PushSubscriptions.Add(subscription);
        else existing.UpdateKeys(subscription.P256dh, subscription.Auth);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string userId, string endpoint, CancellationToken cancellationToken) =>
        await dbContext.PushSubscriptions.Where(subscription => subscription.UserId == Domain.ValueObjects.UserId.Create(userId)
            && subscription.Endpoint == endpoint).ExecuteDeleteAsync(cancellationToken);
}
