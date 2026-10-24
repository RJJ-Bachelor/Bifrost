using EirService.Sessions.Application.Repositories;
using EirService.Sessions.Domain.DomainEvents;
using EirService.Sessions.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared.Application.Persistence;
using Shared.Infrastructure.Persistence.UnitOfWork;

namespace EirService.Sessions.Infrastructure.Persistence;

public sealed class SessionUnitOfWork(EirSessionDbContext dbContext, TimeProvider timeProvider)
    : IUnitOfWork<ISessionRepository>
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var generations = dbContext.ChangeTracker.Entries<Session>()
            .SelectMany(entry => entry.Entity.GetDomainEvents())
            .Select(domainEvent => domainEvent switch
            {
                SessionArchivedDomainEvent archived => (archived.SessionId, Generation: archived.PreviousGeneration),
                SessionClearedDomainEvent cleared => (cleared.SessionId, Generation: cleared.PreviousGeneration),
                _ => (SessionId: Guid.Empty, Generation: 0)
            })
            .Where(generation => generation.Generation > 0).Distinct()
            .OrderBy(generation => generation.SessionId).ThenBy(generation => generation.Generation).ToArray();
        await using var transaction = generations.Length > 0 && dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        // Coordinate the generation change with HelpRequests queue writes and archive cleanup.
        foreach (var generation in generations)
        {
            var queueKey = $"helprequests:{generation.SessionId}:{generation.Generation}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({queueKey}, 0))", cancellationToken);
        }
        try
        {
            await new UnitOfWork<ISessionRepository, EirSessionDbContext>(dbContext, timeProvider)
                .SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_archives_SessionId_Generation" })
        {
            // Concurrent archives can reach the unique generation index before the session concurrency token.
            throw new UnitOfWorkConcurrencyException(exception);
        }
    }
}
