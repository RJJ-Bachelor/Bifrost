using EirService.Participants.Domain.Entities;

namespace EirService.Participants.Application.Repositories;

public interface IParticipantRepository
{
    Task JoinAsync(SessionParticipant participant, CancellationToken cancellationToken);
    Task<bool> IsParticipantAsync(Guid sessionId, string studentId, CancellationToken cancellationToken);
    Task<Guid[]> GetSessionIdsAsync(string studentId, CancellationToken cancellationToken);
}
