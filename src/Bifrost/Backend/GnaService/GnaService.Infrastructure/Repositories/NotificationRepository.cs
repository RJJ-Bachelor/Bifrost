using Microsoft.EntityFrameworkCore;
using GnaService.Application.Repositories;
using GnaService.Domain.Entities;
using GnaService.Infrastructure.Persistence;

namespace GnaService.Infrastructure.Repositories;

public sealed class NotificationRepository(
    GnaNotificationDbContext dbContext) : INotificationRepository
{
    public async Task<bool> TryAddAsync(
        Notification notification,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Notifications.AnyAsync(existing => existing.Id == notification.Id, cancellationToken)) return false;
        await dbContext.Notifications.AddAsync(notification, cancellationToken);
        return true;
    }
}
