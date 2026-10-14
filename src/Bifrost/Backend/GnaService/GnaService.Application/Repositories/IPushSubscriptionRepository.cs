using GnaService.Domain.Entities;

namespace GnaService.Application.Repositories;

public interface IPushSubscriptionRepository
{
    Task<IReadOnlyList<PushSubscription>> GetForUserAsync(string userId, CancellationToken cancellationToken);
    Task SaveAsync(PushSubscription subscription, CancellationToken cancellationToken);
    Task RemoveAsync(string userId, string endpoint, CancellationToken cancellationToken);
}
