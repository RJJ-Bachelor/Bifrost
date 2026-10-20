using EirService.Participants.Domain.Entities;

namespace EirService.Participants.Application.Repositories;

public interface IParticipantRepository
{
    Task JoinAsync(SessionParticipant participant, CancellationToken cancellationToken);
    Task<bool> IsParticipantAsync(Guid sessionId, string subjectId, ParticipantRole role, CancellationToken cancellationToken);
    Task<Guid[]> GetSessionIdsAsync(string subjectId, ParticipantRole role, CancellationToken cancellationToken);
    Task<string[]> GetSubjectIdsAsync(Guid sessionId, ParticipantRole role, CancellationToken cancellationToken);
}
