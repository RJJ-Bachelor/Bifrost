using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Persistence;

namespace EirService.HelpRequests.Infrastructure.Repositories;

public sealed class HelpRequestRepository(
    EirRequestDbContext dbContext, IUnitOfWork<IHelpRequestRepository> unitOfWork, TimeProvider timeProvider)
    : IHelpRequestRepository
{
    public async Task AddAsync(HelpRequest request, CancellationToken cancellationToken) =>
        await dbContext.Requests.AddAsync(request, cancellationToken);

    public Task<HelpRequest?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Requests.Include(request => request.Messages).SingleOrDefaultAsync(request => request.Id == id, cancellationToken);

    public async Task<IReadOnlyList<HelpRequest>> GetByUserIdAsync(string studentId, CancellationToken cancellationToken) =>
        await dbContext.Requests.AsNoTracking().Include(request => request.Messages)
            .Where(request => request.StudentId == studentId).OrderBy(request => request.QueuedAt).ThenBy(request => request.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<HelpRequest>> GetForSessionAsync(Guid sessionId, int generation, CancellationToken cancellationToken) =>
        await dbContext.Requests.AsNoTracking().Include(request => request.Messages)
            .Where(request => request.SessionId == sessionId && request.Generation == generation)
            .OrderByDescending(request => request.ReturnedToQueueAt.HasValue)
            .ThenByDescending(request => request.ReturnedToQueueAt)
            .ThenBy(request => request.QueuedAt).ThenBy(request => request.Id).ToListAsync(cancellationToken);

    public Task<bool> HasOpenAsync(Guid sessionId, int generation, string studentId, CancellationToken cancellationToken) =>
        dbContext.Requests.AnyAsync(request => request.SessionId == sessionId && request.Generation == generation &&
            request.StudentId == studentId && (request.Status == HelpRequestStatus.Pending ||
            request.Status == HelpRequestStatus.Active || request.Status == HelpRequestStatus.AwaitingConfirmation), cancellationToken);

    public async Task<HelpRequest?> TakeNextAsync(Guid sessionId, int generation, string teacherId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Lock only the selected row, skipping rows currently being reserved by another caller.
        var candidates = await dbContext.Requests.FromSqlInterpolated($"""
            SELECT r.*, r.xmin FROM helprequests."HelpRequest" AS r
            WHERE r."SessionId" = {sessionId} AND r."Generation" = {generation}
                AND r."Status" = 'Pending' AND r."AssignedTeacherId" IS NULL
            ORDER BY r."ReturnedToQueueAt" DESC NULLS LAST, r."QueuedAt", r."Id"
            LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        var request = candidates.SingleOrDefault();
        if (request is null) return null;
        await dbContext.Entry(request).Collection(request => request.Messages).LoadAsync(cancellationToken);
        request.Reserve(teacherId, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return request;
    }
}
