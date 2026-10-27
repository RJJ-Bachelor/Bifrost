using System.Text.Json;
using EirService.Contracts.HelpRequests.GetArchivedConversations;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EirService.HelpRequests.Infrastructure.Repositories;

public sealed class ArchivedConversationRepository(EirRequestDbContext dbContext) : IArchivedConversationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ArchiveQueueAsync(Guid sessionId, int generation, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var queueKey = $"helprequests:{sessionId}:{generation}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({queueKey}, 0))", cancellationToken);
        var requests = await dbContext.Requests.Include(request => request.Messages)
            .Where(request => request.SessionId == sessionId && request.Generation == generation &&
                (request.Status == HelpRequestStatus.Pending || request.Status == HelpRequestStatus.Active ||
                 request.Status == HelpRequestStatus.AwaitingFaq || request.Status == HelpRequestStatus.AwaitingConfirmation))
            .ToArrayAsync(cancellationToken);
        foreach (var request in requests.Where(request => request.StudentId is not null &&
                     request.Messages.Any(message => message.AuthorRole == AuthorRole.Teacher && !message.IsRejected)))
        {
            dbContext.ArchivedConversations.Add(new ArchivedConversationSnapshot
            {
                Id = request.Id, SessionId = request.SessionId, Generation = request.Generation,
                StudentId = request.StudentId!, Data = JsonSerializer.Serialize(ToConversation(request), JsonOptions)
            });
        }
        // Copying conversations and deleting the queue are committed together. Redelivery finds an empty queue.
        dbContext.Requests.RemoveRange(requests);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ArchivedConversation>> GetForGenerationAsync(
        Guid sessionId, int generation, string? studentId, CancellationToken cancellationToken)
    {
        var requests = await ArchivedRequests(sessionId, studentId).Include(request => request.Messages)
            .Where(request => request.Generation == generation).ToArrayAsync(cancellationToken);
        // Read live requests first: a concurrent cleanup may add snapshots, but cannot make a conversation disappear.
        var snapshots = await ArchivedSnapshots(sessionId, studentId)
            .Where(snapshot => snapshot.Generation == generation).ToArrayAsync(cancellationToken);
        return requests.Select(ToConversation)
            .Concat(snapshots.Select(snapshot => JsonSerializer.Deserialize<ArchivedConversation>(snapshot.Data, JsonOptions)
                ?? throw new InvalidDataException("An archived conversation could not be read.")))
            .DistinctBy(conversation => conversation.Id)
            .OrderBy(conversation => conversation.CreatedAt).ThenBy(conversation => conversation.Id).ToArray();
    }

    public async Task<IReadOnlyDictionary<int, int>> CountForGenerationsAsync(
        Guid sessionId, int[] generations, string? studentId, CancellationToken cancellationToken)
    {
        if (generations.Length == 0) return new Dictionary<int, int>();
        var requests = ArchivedRequests(sessionId, studentId)
            .Where(request => generations.Contains(request.Generation))
            .Select(request => new { request.Id, request.Generation });
        var snapshots = ArchivedSnapshots(sessionId, studentId)
            .Where(snapshot => generations.Contains(snapshot.Generation))
            .Select(snapshot => new { snapshot.Id, snapshot.Generation });
        // One SQL statement sees either side of queue cleanup without losing or double-counting conversations.
        return await requests.Union(snapshots).GroupBy(conversation => conversation.Generation)
            .Select(group => new { Generation = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Generation, group => group.Count, cancellationToken);
    }

    private IQueryable<HelpRequest> ArchivedRequests(Guid sessionId, string? studentId) =>
        dbContext.Requests.AsNoTracking().Where(request => request.SessionId == sessionId &&
            request.StudentId != null && (studentId == null || request.StudentId == studentId) &&
            (request.Status == HelpRequestStatus.CompletedByFaq || request.Status == HelpRequestStatus.CompletedByDigital ||
             request.Status == HelpRequestStatus.CompletedByPhysical || request.Status == HelpRequestStatus.Cancelled ||
             request.Messages.Any(message => message.AuthorRole == AuthorRole.Teacher && !message.IsRejected)));

    private IQueryable<ArchivedConversationSnapshot> ArchivedSnapshots(Guid sessionId, string? studentId) =>
        dbContext.ArchivedConversations.AsNoTracking().Where(snapshot => snapshot.SessionId == sessionId &&
            (studentId == null || snapshot.StudentId == studentId));

    private static ArchivedConversation ToConversation(HelpRequest request) => new(
        request.Id, request.SessionId, request.Generation, request.RequestNumber, request.DisplayNumber,
        request.StudentId!, request.HelpType.ToString(), request.Status.ToString(), request.AssignedTeacherId,
        request.CreatedAt, request.QueuedAt, request.ReturnedToQueueAt, request.StartedAt, request.ClosedAt, request.FaqAnswerId,
        request.Messages.Where(message => !message.IsRejected).OrderBy(message => message.SequenceNumber)
            .Select(message => new ArchivedConversationMessage(message.Id, message.HelpRequestId, message.AuthorId,
                message.AuthorRole.ToString(), message.MessageType.ToString(), message.Text.Value,
                message.CreatedAt, message.SequenceNumber)).ToArray());
}
