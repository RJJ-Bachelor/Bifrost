using EirService.Sessions.Application.Repositories;
using EirService.Sessions.Domain.Entities;
using EirService.Sessions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using EirService.Contracts.Sessions.FindSessionByCode;
using EirService.Sessions.Domain.ValueObjects;

namespace EirService.Sessions.Infrastructure.Repositories;

public sealed class SessionRepository(EirSessionDbContext dbContext) : ISessionRepository
{
    public async Task<bool> TryAddAsync(Session session, CancellationToken cancellationToken)
    {
        dbContext.Sessions.Add(session);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_sessions_Code" or "IX_sessions_TeacherCode" })
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public void Remove(Session session) => dbContext.Sessions.Remove(session);

    public Task<Session?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Sessions.SingleOrDefaultAsync(session => session.Id == id, cancellationToken);

    public Task<Session?> GetByCodeAsync(string code, SessionCodeType codeType, CancellationToken cancellationToken)
    {
        var value = new SessionCode(code);
        var sessions = dbContext.Sessions.AsNoTracking();
        return codeType switch
        {
            SessionCodeType.Student => sessions.SingleOrDefaultAsync(session => session.Code == value, cancellationToken),
            SessionCodeType.Teacher => sessions.SingleOrDefaultAsync(session => session.TeacherCode == value, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(codeType))
        };
    }

    public async Task<IReadOnlyList<Session>> GetByTeacherAsync(string teacherId, CancellationToken cancellationToken) =>
        await dbContext.Sessions.AsNoTracking().Where(session => session.CreatedByTeacherId == teacherId)
            .OrderByDescending(session => session.CreatedAtUtc).ThenBy(session => session.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Session>> GetByIdsAsync(Guid[] ids, CancellationToken cancellationToken) =>
        await dbContext.Sessions.AsNoTracking().Where(session => ids.Contains(session.Id))
            .OrderByDescending(session => session.CreatedAtUtc).ThenBy(session => session.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SessionArchive>> GetArchivesAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await dbContext.Archives.AsNoTracking().Where(archive => archive.SessionId == sessionId)
            .OrderByDescending(archive => archive.Generation).ToArrayAsync(cancellationToken);

    public Task<SessionArchive?> GetArchiveAsync(Guid sessionId, Guid archiveId, CancellationToken cancellationToken) =>
        dbContext.Archives.AsNoTracking().SingleOrDefaultAsync(
            archive => archive.SessionId == sessionId && archive.Id == archiveId, cancellationToken);
}
