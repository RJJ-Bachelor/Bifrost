using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using EirService.Participants.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EirService.Participants.Infrastructure.Repositories;

public sealed class StudentIdentityTransferRepository(EirParticipantDbContext dbContext) : IStudentIdentityTransferRepository
{
    public async Task<bool> TryAddAsync(StudentIdentityTransfer transfer, CancellationToken cancellationToken)
    {
        var now = transfer.ExpiresAtUtc.Subtract(StudentIdentityTransfer.Lifetime);
        await dbContext.IdentityTransfers.Where(existing => existing.ExpiresAtUtc <= now).ExecuteDeleteAsync(cancellationToken);

        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO participants.identity_transfers ("Code", "StudentId", "ExpiresAtUtc")
            VALUES ({transfer.Code}, {transfer.StudentId}, {transfer.ExpiresAtUtc})
            ON CONFLICT ("Code") DO NOTHING;
            """, cancellationToken);
        return inserted == 1;
    }

    public async Task<string?> RedeemAsync(string code, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var utcNow = now.ToUniversalTime();
        // Deleting and returning in one statement permits only one device to redeem a code.
        var studentIds = await dbContext.Database.SqlQuery<string>($"""
            DELETE FROM participants.identity_transfers
            WHERE "Code" = {code} AND "ExpiresAtUtc" > {utcNow}
            RETURNING "StudentId" AS "Value";
            """).ToArrayAsync(cancellationToken);
        return studentIds.SingleOrDefault();
    }
}
