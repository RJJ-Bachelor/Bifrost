using EirService.Contracts.Exceptions;
using EirService.Contracts.Sessions.GetSessionGenerations;
using EirService.KnowledgeBase.Application.Repositories;
using EirService.KnowledgeBase.Domain.Entities;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Persistence;
using Shared.Infrastructure.Persistence.UnitOfWork;

namespace EirService.KnowledgeBase.Infrastructure.Persistence;

public sealed class KnowledgeBaseUnitOfWork(EirKnowledgeBaseDbContext dbContext, TimeProvider timeProvider)
    : IUnitOfWork<IFaqRepository>
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Only newly published teacher FAQs require the current generation. Delayed
        // broker answers must still be allowed to complete an archived FAQ.
        var generationsToLock = dbContext.ChangeTracker.Entries<FaqAnswer>()
            .Where(entry => entry.State == EntityState.Added && entry.Entity.Answer is not null &&
                entry.Entity.HelpRequestIds.Count == 0)
            .Select(entry => (entry.Entity.SessionId, entry.Entity.Generation)).Distinct()
            .OrderBy(faq => faq.SessionId).ThenBy(faq => faq.Generation).ToArray();
        await using var transaction = generationsToLock.Length > 0 && dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        foreach (var generation in generationsToLock)
        {
            var queueKey = $"helprequests:{generation.SessionId}:{generation.Generation}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({queueKey}, 0))", cancellationToken);
        }
        if (generationsToLock.Length > 0)
        {
            var generations = await new GetSessionGenerationsQuery(
                generationsToLock.Select(faq => faq.SessionId).Distinct().ToArray()).ExecuteAsync(cancellationToken);
            if (generationsToLock.Any(faq => !generations.TryGetValue(faq.SessionId, out var current) || current != faq.Generation))
                throw new OperationConflictException("The session generation changed. Refresh and try again.");
        }

        await new UnitOfWork<IFaqRepository, EirKnowledgeBaseDbContext>(dbContext, timeProvider)
            .SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }
}
