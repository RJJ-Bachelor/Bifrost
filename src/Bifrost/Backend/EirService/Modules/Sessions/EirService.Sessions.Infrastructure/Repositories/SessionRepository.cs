using EirService.Sessions.Application.Repositories;
using EirService.Sessions.Domain.Entities;
using EirService.Sessions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_sessions_Code" })
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public Task<Session?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Sessions.SingleOrDefaultAsync(session => session.Id == id, cancellationToken);

    public Task<Session?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        dbContext.Sessions.AsNoTracking().SingleOrDefaultAsync(session => session.Code == new Domain.ValueObjects.SessionCode(code), cancellationToken);

    public async Task<IReadOnlyList<Session>> GetByTeacherAsync(string teacherId, CancellationToken cancellationToken) =>
        await dbContext.Sessions.AsNoTracking().Where(session => session.CreatedByTeacherId == teacherId)
            .OrderByDescending(session => session.CreatedAtUtc).ThenBy(session => session.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Session>> GetByIdsAsync(Guid[] ids, CancellationToken cancellationToken) =>
        await dbContext.Sessions.AsNoTracking().Where(session => ids.Contains(session.Id))
            .OrderByDescending(session => session.CreatedAtUtc).ThenBy(session => session.Id).ToListAsync(cancellationToken);

    public async Task<bool> TryClearAsync(Session session, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }
}
