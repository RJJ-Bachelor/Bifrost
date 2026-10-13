using EirService.Contracts.Exceptions;
using EirService.HelpRequests.Application.Repositories;
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
        // Message-only changes must also update the aggregate's xmin concurrency token.
        foreach (var entry in dbContext.ChangeTracker.Entries<HelpRequest>()
            .Where(entry => entry.State == EntityState.Unchanged && entry.Entity.GetDomainEvents().Count > 0))
            entry.Property(request => request.QueuedAt).IsModified = true;
        try
        {
            await new UnitOfWork<IHelpRequestRepository, EirRequestDbContext>(dbContext, timeProvider).SaveChangesAsync(cancellationToken);
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
