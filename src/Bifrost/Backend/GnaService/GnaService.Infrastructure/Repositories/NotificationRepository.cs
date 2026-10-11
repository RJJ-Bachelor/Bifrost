using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        if (await dbContext.Notifications.AnyAsync(existing => existing.Id == notification.Id, cancellationToken)) return;
        await dbContext.Notifications.AddAsync(notification, cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_notifications" })
        {
            dbContext.ChangeTracker.Clear();
        }
    }
}
