using Microsoft.EntityFrameworkCore;
using MimirService.Application.Repositories;
using MimirService.Domain.Entities;
using MimirService.Infrastructure.Persistence;
using Npgsql;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace MimirService.Infrastructure.Repositories;

public sealed class HelpRequestRepository(MimirRequestDbContext dbContext, TimeProvider timeProvider) : IHelpRequestRepository
{
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Requests.AnyAsync(existing => existing.Id == id, cancellationToken);

    public async Task<IReadOnlyList<HelpRequest>> GetWaitingAsync(Guid sessionId, int generation,
        CancellationToken cancellationToken) =>
        await dbContext.Requests
            .Where(request => request.SessionId == sessionId && request.Generation == generation &&
                request.GeneralizationId == null)
            .OrderBy(request => request.ReceivedAt)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Generalization>> GetGeneralizationsAsync(Guid sessionId, int generation,
        CancellationToken cancellationToken) =>
        await dbContext.Generalizations
            .Where(generalization => generalization.SessionId == sessionId && generalization.Generation == generation)
            .OrderBy(generalization => generalization.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetGeneralizationMemberIdsAsync(Guid generalizationId,
        CancellationToken cancellationToken) =>
        await dbContext.Requests
            .Where(request => request.GeneralizationId == generalizationId)
            .Select(request => request.Id)
            .ToArrayAsync(cancellationToken);

    public Task<Generalization?> GetGeneralizationAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Generalizations.SingleOrDefaultAsync(generalization => generalization.Id == id, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public async Task<bool> TryAddAsync(HelpRequest request, Generalization? generalization, OutgoingMessage? reply,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Requests.AnyAsync(existing => existing.Id == request.Id, cancellationToken)) return false;
        if (generalization is not null) dbContext.Generalizations.Add(generalization);
        dbContext.Requests.Add(request);
        if (reply is not null) dbContext.Outbox.Add(OutboxMessage.Create(reply, timeProvider.GetUtcNow()));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_requests" })
        {
            // The original request, generalization and reply were committed together. A redelivery needs no new reply.
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<(int Requests, int Generalizations)> DeleteSessionDataAsync(Guid sessionId, int? beforeGeneration,
        CancellationToken cancellationToken)
    {
        var requests = dbContext.Requests.Where(request => request.SessionId == sessionId);
        var generalizations = dbContext.Generalizations.Where(generalization => generalization.SessionId == sessionId);
        if (beforeGeneration is { } generation)
        {
            requests = requests.Where(request => request.Generation < generation);
            generalizations = generalizations.Where(generalization => generalization.Generation < generation);
        }

        // Requests reference generalizations, so they go first. If the second delete fails, the redelivered message deletes the rest.
        var deletedRequests = await requests.ExecuteDeleteAsync(cancellationToken);
        var deletedGeneralizations = await generalizations.ExecuteDeleteAsync(cancellationToken);
        return (deletedRequests, deletedGeneralizations);
    }
}
