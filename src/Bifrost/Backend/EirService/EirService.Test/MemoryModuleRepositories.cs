using Shared.Application.Persistence;
using EirService.Sessions.Application.Repositories;
using EirService.Sessions.Domain.Entities;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using EirService.Contracts.Sessions.FindSessionByCode;

namespace EirService.Test;

internal sealed class MemorySessionRepository : ISessionRepository, IUnitOfWork<ISessionRepository>
{
    private readonly List<Session> sessions = [];
    private readonly List<Session> pendingDeletes = [];
    public Task<bool> TryAddAsync(Session session, CancellationToken cancellationToken)
    {
        sessions.Add(session);
        return Task.FromResult(true);
    }
    public void Remove(Session session) => pendingDeletes.Add(session);
    public Task<Session?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(sessions.SingleOrDefault(session => session.Id == id));
    public Task<Session?> GetByCodeAsync(string code, SessionCodeType codeType, CancellationToken cancellationToken) =>
        Task.FromResult(sessions.SingleOrDefault(session => (codeType == SessionCodeType.Teacher ? session.TeacherCode.Value : session.Code.Value) == code));
    public Task<IReadOnlyList<Session>> GetByTeacherAsync(string teacherId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Session>>(sessions.Where(session => session.CreatedByTeacherId == teacherId).ToArray());
    public Task<IReadOnlyList<Session>> GetByIdsAsync(Guid[] ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Session>>(sessions.Where(session => ids.Contains(session.Id)).ToArray());
    public Task<IReadOnlyList<SessionArchive>> GetArchivesAsync(Guid sessionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SessionArchive>>(sessions.Where(session => session.Id == sessionId)
            .SelectMany(session => session.Archives).OrderByDescending(archive => archive.Generation).ToArray());
    public Task<SessionArchive?> GetArchiveAsync(Guid sessionId, Guid archiveId, CancellationToken cancellationToken) =>
        Task.FromResult(sessions.Where(session => session.Id == sessionId).SelectMany(session => session.Archives)
            .SingleOrDefault(archive => archive.Id == archiveId));
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var session in sessions) session.ClearDomainEvents();
        foreach (var session in pendingDeletes) sessions.Remove(session);
        pendingDeletes.Clear();
        return Task.CompletedTask;
    }
}

internal sealed class MemoryParticipantRepository : IParticipantRepository
{
    private readonly List<SessionParticipant> participants = [];
    public Task JoinAsync(SessionParticipant participant, CancellationToken cancellationToken)
    {
        if (!participants.Any(existing => existing.Id == participant.Id)) participants.Add(participant);
        return Task.CompletedTask;
    }
    public Task<bool> IsParticipantAsync(Guid sessionId, string subjectId, ParticipantRole role, CancellationToken cancellationToken) =>
        Task.FromResult(participants.Any(participant => participant.SessionId == sessionId && participant.SubjectId == subjectId && participant.Role == role));
    public Task<Guid[]> GetSessionIdsAsync(string subjectId, ParticipantRole role, CancellationToken cancellationToken) =>
        Task.FromResult(participants.Where(participant => participant.SubjectId == subjectId && participant.Role == role).Select(participant => participant.SessionId).ToArray());
    public Task<string[]> GetSubjectIdsAsync(Guid sessionId, ParticipantRole role, CancellationToken cancellationToken) =>
        Task.FromResult(participants.Where(participant => participant.SessionId == sessionId && participant.Role == role).Select(participant => participant.SubjectId).ToArray());
}

[CollectionDefinition("FastEndpoints", DisableParallelization = true)]
public sealed class FastEndpointsCollection;
