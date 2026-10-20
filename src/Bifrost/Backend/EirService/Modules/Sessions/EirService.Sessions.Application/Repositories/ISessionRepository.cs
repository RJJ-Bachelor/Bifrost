using EirService.Sessions.Domain.Entities;
using EirService.Contracts.Sessions.FindSessionByCode;

namespace EirService.Sessions.Application.Repositories;

public interface ISessionRepository
{
    Task<bool> TryAddAsync(Session session, CancellationToken cancellationToken);
    Task<Session?> GetAsync(Guid id, CancellationToken cancellationToken);
    void Remove(Session session);
    Task<Session?> GetByCodeAsync(string code, SessionCodeType codeType, CancellationToken cancellationToken);
    Task<IReadOnlyList<Session>> GetByTeacherAsync(string teacherId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Session>> GetByIdsAsync(Guid[] ids, CancellationToken cancellationToken);
}
