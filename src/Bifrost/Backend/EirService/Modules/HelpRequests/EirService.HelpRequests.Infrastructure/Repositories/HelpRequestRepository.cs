using EirService.Contracts.HelpRequests.GetWaitingCounts;
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
        await SessionRequests(sessionId, generation).Include(request => request.Messages).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<HelpRequestActivity>> GetSessionActivityAsync(Guid sessionId, int generation, CancellationToken cancellationToken) =>
        await SessionRequests(sessionId, generation)
            .Select(request => new HelpRequestActivity(request.Id, request.StudentId, request.RequestNumber,
                request.HelpType, request.Status, request.AssignedTeacherId, request.QueuedAt, request.StartedAt, request.ClosedAt))
            .ToListAsync(cancellationToken);

    private IOrderedQueryable<HelpRequest> SessionRequests(Guid sessionId, int generation) =>
        dbContext.Requests.AsNoTracking()
            .Where(request => request.SessionId == sessionId && request.Generation == generation)
            .OrderByDescending(request => request.StudentId == null)
            .ThenByDescending(request => request.ReturnedToQueueAt.HasValue)
            .ThenByDescending(request => request.ReturnedToQueueAt)
            .ThenBy(request => request.QueuedAt).ThenBy(request => request.Id);

    public async Task<IReadOnlyList<HelpRequest>> GetByIdsAsync(Guid[] ids, CancellationToken cancellationToken) =>
        await dbContext.Requests.Include(request => request.Messages).Where(request => ids.Contains(request.Id)).ToArrayAsync(cancellationToken);

    public Task<string[]> GetWaitingStudentIdsAsync(Guid sessionId, int generation, CancellationToken cancellationToken) =>
        dbContext.Requests.AsNoTracking()
            .Where(request => request.SessionId == sessionId && request.Generation == generation &&
                request.Status == HelpRequestStatus.Pending && request.StudentId != null)
            .Select(request => request.StudentId!).Distinct().ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, WaitingCounts>> GetWaitingCountsAsync(
        IReadOnlyDictionary<Guid, int> sessionGenerations, CancellationToken cancellationToken)
    {
        if (sessionGenerations.Count == 0) return new Dictionary<Guid, WaitingCounts>();

        IQueryable<HelpRequest>? waitingRequests = null;
        foreach (var group in sessionGenerations.GroupBy(session => session.Value))
        {
            var generation = group.Key;
            var sessionIds = group.Select(session => session.Key).ToArray();
            var requests = dbContext.Requests.AsNoTracking()
                .Where(request => request.Status == HelpRequestStatus.Pending && request.Generation == generation &&
                    sessionIds.Contains(request.SessionId));
            waitingRequests = waitingRequests is null ? requests : waitingRequests.Concat(requests);
        }

        // Aggregate exact session/generation pairs in one SQL query, without loading requests or messages.
        return await waitingRequests!.GroupBy(request => request.SessionId)
            .Select(group => new
            {
                SessionId = group.Key,
                WaitingCount = group.Count(),
                UnreservedWaitingCount = group.Count(request => request.AssignedTeacherId == null)
            })
            .ToDictionaryAsync(group => group.SessionId,
                group => new WaitingCounts(group.WaitingCount, group.UnreservedWaitingCount), cancellationToken);
    }

    public async Task<IReadOnlyList<HelpRequest>> GetLinkedToFaqAsync(Guid faqId, CancellationToken cancellationToken) =>
        await dbContext.Requests.Include(request => request.Messages)
            .Where(request => request.FaqAnswerId == faqId && request.StudentId != null).ToArrayAsync(cancellationToken);

    public Task<bool> HasOpenAsync(Guid sessionId, int generation, string studentId, CancellationToken cancellationToken) =>
        dbContext.Requests.AnyAsync(request => request.SessionId == sessionId && request.Generation == generation &&
            request.StudentId == studentId && (request.Status == HelpRequestStatus.Pending ||
            request.Status == HelpRequestStatus.Active || request.Status == HelpRequestStatus.AwaitingFaq || request.Status == HelpRequestStatus.AwaitingConfirmation), cancellationToken);

    public async Task<HelpRequest?> TakeNextAsync(Guid sessionId, int generation, string teacherId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Acquire the queue lock before locking a row, matching archival cleanup's lock order.
        var queueKey = $"helprequests:{sessionId}:{generation}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({queueKey}, 0))", cancellationToken);
        // A retry resumes the teacher's current request instead of reserving another one.
        var reserved = await dbContext.Requests.Include(request => request.Messages)
            .Where(request => request.SessionId == sessionId && request.Generation == generation &&
                request.AssignedTeacherId == teacherId &&
                (request.Status == HelpRequestStatus.Pending || request.Status == HelpRequestStatus.Active))
            .OrderByDescending(request => request.Status == HelpRequestStatus.Active)
            .ThenBy(request => request.QueuedAt).ThenBy(request => request.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (reserved is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return reserved;
        }
        // Lock only the selected row, skipping rows currently being reserved by another caller.
        var candidates = await dbContext.Requests.FromSqlInterpolated($"""
            SELECT r.*, r.xmin FROM helprequests."HelpRequest" AS r
            WHERE r."SessionId" = {sessionId} AND r."Generation" = {generation}
                AND r."Status" = 'Pending' AND r."AssignedTeacherId" IS NULL
            ORDER BY (r."StudentId" IS NULL) DESC, r."ReturnedToQueueAt" DESC NULLS LAST, r."QueuedAt", r."Id"
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
