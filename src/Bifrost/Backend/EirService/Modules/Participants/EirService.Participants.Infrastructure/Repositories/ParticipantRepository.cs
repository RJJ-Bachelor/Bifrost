using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using EirService.Participants.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EirService.Participants.Infrastructure.Repositories;

public sealed class ParticipantRepository(EirParticipantDbContext dbContext) : IParticipantRepository
{
    public async Task JoinAsync(SessionParticipant participant, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Participants.SingleOrDefaultAsync(member => member.SessionId == participant.SessionId &&
            member.SubjectId == participant.SubjectId && member.Role == participant.Role, cancellationToken);
        if (existing is not null)
        {
            existing.UpdateDisplayName(participant.DisplayName);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }
        dbContext.Participants.Add(participant);
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_session_participants" })
        {
            // Joining twice, including concurrent joins, preserves the original membership.
            dbContext.ChangeTracker.Clear();
            await JoinAsync(participant, cancellationToken);
        }
    }

    public Task<bool> IsParticipantAsync(Guid sessionId, string subjectId, ParticipantRole role, CancellationToken cancellationToken) =>
        dbContext.Participants.AsNoTracking().AnyAsync(participant => participant.SessionId == sessionId &&
            participant.SubjectId == subjectId && participant.Role == role, cancellationToken);

    public Task<Guid[]> GetSessionIdsAsync(string subjectId, ParticipantRole role, CancellationToken cancellationToken) =>
        dbContext.Participants.AsNoTracking().Where(participant => participant.SubjectId == subjectId && participant.Role == role)
            .Select(participant => participant.SessionId).ToArrayAsync(cancellationToken);

    public Task<string[]> GetParticipantIdsAsync(Guid sessionId, CancellationToken cancellationToken) =>
        dbContext.Participants.AsNoTracking().Where(participant => participant.SessionId == sessionId)
            .Select(participant => participant.SubjectId).Distinct().OrderBy(subjectId => subjectId).ToArrayAsync(cancellationToken);

    public Task<string[]> GetSubjectIdsAsync(Guid sessionId, ParticipantRole role, CancellationToken cancellationToken) =>
        dbContext.Participants.AsNoTracking().Where(participant => participant.SessionId == sessionId && participant.Role == role)
            .OrderBy(participant => participant.SubjectId).Select(participant => participant.SubjectId).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<SessionParticipant>> GetTeachersAsync(Guid[] sessionIds, CancellationToken cancellationToken) =>
        await dbContext.Participants.AsNoTracking().Where(participant => sessionIds.Contains(participant.SessionId) &&
            participant.Role == ParticipantRole.Teacher).ToArrayAsync(cancellationToken);
}
