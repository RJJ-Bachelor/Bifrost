using Shared.Application.Persistence;
using EirService.Sessions.Application.Repositories;
using EirService.Sessions.Domain.Entities;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;

namespace EirService.Test;

internal sealed class MemorySessionRepository : ISessionRepository, IUnitOfWork<ISessionRepository>
{
    private readonly List<Session> sessions = [];
    public Task<bool> TryAddAsync(Session session, CancellationToken cancellationToken)
    {
        sessions.Add(session);
        return Task.FromResult(true);
    }
    public Task<Session?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(sessions.SingleOrDefault(session => session.Id == id));
    public Task<Session?> GetByCodeAsync(string code, CancellationToken cancellationToken) => Task.FromResult(sessions.SingleOrDefault(session => session.Code.Value == code));
    public Task<IReadOnlyList<Session>> GetByTeacherAsync(string teacherId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Session>>(sessions.Where(session => session.CreatedByTeacherId == teacherId).ToArray());
    public Task<IReadOnlyList<Session>> GetByIdsAsync(Guid[] ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Session>>(sessions.Where(session => ids.Contains(session.Id)).ToArray());
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var session in sessions) session.ClearDomainEvents();
        return Task.CompletedTask;
    }
}

internal sealed class MemoryParticipantRepository : IParticipantRepository
{
    private readonly List<SessionParticipant> participants = [];
    public Task JoinAsync(SessionParticipant participant, CancellationToken cancellationToken)
    {
        if (!participants.Any(existing => existing.SessionId == participant.SessionId && existing.StudentSubjectId == participant.StudentSubjectId)) participants.Add(participant);
        return Task.CompletedTask;
    }
    public Task<bool> IsParticipantAsync(Guid sessionId, string studentId, CancellationToken cancellationToken) =>
        Task.FromResult(participants.Any(participant => participant.SessionId == sessionId && participant.StudentSubjectId == studentId));
    public Task<Guid[]> GetSessionIdsAsync(string studentId, CancellationToken cancellationToken) =>
        Task.FromResult(participants.Where(participant => participant.StudentSubjectId == studentId).Select(participant => participant.SessionId).ToArray());
}

[CollectionDefinition("FastEndpoints", DisableParallelization = true)]
public sealed class FastEndpointsCollection;
