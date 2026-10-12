using EirService.Sessions.Domain.Entities;

namespace EirService.Sessions.Application.Repositories;

public interface ISessionRepository
{
    Task<bool> TryAddAsync(Session session, CancellationToken cancellationToken);
    Task<Session?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Session?> GetByCodeAsync(string code, CancellationToken cancellationToken);
    Task<IReadOnlyList<Session>> GetByTeacherAsync(string teacherId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Session>> GetByIdsAsync(Guid[] ids, CancellationToken cancellationToken);
}
