using EirService.Contracts.Exceptions;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.DomainEvents;
using EirService.HelpRequests.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared.Application.Persistence;
using Shared.Infrastructure.Persistence.UnitOfWork;

namespace EirService.HelpRequests.Infrastructure.Persistence;

public sealed class HelpRequestUnitOfWork(EirRequestDbContext dbContext, TimeProvider timeProvider)
    : IUnitOfWork<IHelpRequestRepository>
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var queuesToLock = dbContext.ChangeTracker.Entries<HelpRequest>()
            .Where(entry => entry.State == EntityState.Added || entry.Entity.GetDomainEvents()
                .OfType<HelpRequestChangedDomainEvent>().Any(change => change.Change == "StudentMessageAdded"))
            .Select(entry => (entry.Entity.SessionId, entry.Entity.Generation)).Distinct()
            .OrderBy(queue => queue.SessionId).ThenBy(queue => queue.Generation).ToArray();
        await using var transaction = queuesToLock.Length > 0 && dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        // Serialize queue entries, including follow-ups, so only the first notifies the owner.
        foreach (var queue in queuesToLock)
        {
            var queueKey = $"helprequests:{queue.SessionId}:{queue.Generation}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({queueKey}, 0))", cancellationToken);
        }
        // Message-only changes must also update the aggregate's xmin concurrency token.
        foreach (var entry in dbContext.ChangeTracker.Entries<HelpRequest>()
            .Where(entry => entry.State == EntityState.Unchanged && entry.Entity.GetDomainEvents().Count > 0))
            entry.Property(request => request.QueuedAt).IsModified = true;
        try
        {
            await new UnitOfWork<IHelpRequestRepository, EirRequestDbContext>(dbContext, timeProvider).SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (UnitOfWorkConcurrencyException)
        {
            throw new OperationConflictException("The request changed during this operation. Refresh and try again.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_HelpRequest_OneOpenPerStudent" })
        {
            throw new OperationConflictException("You already have an open request in this session.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_HelpRequestMessage_HelpRequestId_SequenceNumber" })
        {
            throw new OperationConflictException("A message was added concurrently. Refresh and try again.");
        }
    }
}
