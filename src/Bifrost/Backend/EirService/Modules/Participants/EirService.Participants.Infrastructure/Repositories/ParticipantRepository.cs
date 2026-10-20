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
        if (await IsParticipantAsync(participant.SessionId, participant.SubjectId, participant.Role, cancellationToken)) return;
        dbContext.Participants.Add(participant);
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_session_participants" })
        {
            // Joining twice, including concurrent joins, preserves the original membership.
            dbContext.ChangeTracker.Clear();
        }
    }

    public Task<bool> IsParticipantAsync(Guid sessionId, string subjectId, ParticipantRole role, CancellationToken cancellationToken) =>
        dbContext.Participants.AsNoTracking().AnyAsync(participant => participant.SessionId == sessionId &&
            participant.SubjectId == subjectId && participant.Role == role, cancellationToken);

    public Task<Guid[]> GetSessionIdsAsync(string subjectId, ParticipantRole role, CancellationToken cancellationToken) =>
        dbContext.Participants.AsNoTracking().Where(participant => participant.SubjectId == subjectId && participant.Role == role)
            .Select(participant => participant.SessionId).ToArrayAsync(cancellationToken);

    public Task<string[]> GetSubjectIdsAsync(Guid sessionId, ParticipantRole role, CancellationToken cancellationToken) =>
        dbContext.Participants.AsNoTracking().Where(participant => participant.SessionId == sessionId && participant.Role == role)
            .OrderBy(participant => participant.SubjectId).Select(participant => participant.SubjectId).ToArrayAsync(cancellationToken);
}
