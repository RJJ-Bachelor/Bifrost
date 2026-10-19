using GnaService.Application.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shared.Application.Persistence;
using Shared.Infrastructure.Persistence.UnitOfWork;

namespace GnaService.Infrastructure.Persistence;

public sealed class NotificationUnitOfWork(GnaNotificationDbContext dbContext, TimeProvider timeProvider,
    ILogger<NotificationUnitOfWork> logger) : IUnitOfWork<INotificationRepository>
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await new UnitOfWork<INotificationRepository, GnaNotificationDbContext>(dbContext, timeProvider)
                .SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_notifications" })
        {
            dbContext.ChangeTracker.Clear();
            logger.LogInformation("Ignored a concurrently received duplicate notification.");
        }
    }
}
