using GnaService.Application.Repositories;
using GnaService.Domain.Entities;
using GnaService.Infrastructure.Persistence;

namespace GnaService.Infrastructure.Repositories;

public sealed class NotificationRepository(
    GnaNotificationDbContext dbContext) : INotificationRepository
{
    public async Task AddAsync(
        Notification notification,
        CancellationToken cancellationToken)
    {
        await dbContext.Notifications.AddAsync(notification, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
