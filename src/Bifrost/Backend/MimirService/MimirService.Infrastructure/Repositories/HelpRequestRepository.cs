using Microsoft.EntityFrameworkCore;
using MimirService.Application.Repositories;
using MimirService.Domain.Entities;
using MimirService.Infrastructure.Persistence;
using Npgsql;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace MimirService.Infrastructure.Repositories;

public sealed class HelpRequestRepository(MimirRequestDbContext dbContext, TimeProvider timeProvider) : IHelpRequestRepository
{
    public async Task<bool> TryAddAsync(HelpRequest request, OutgoingMessage reply, CancellationToken cancellationToken)
    {
        if (await dbContext.Requests.AnyAsync(existing => existing.Id == request.Id, cancellationToken)) return false;
        dbContext.Requests.Add(request);
        dbContext.Outbox.Add(OutboxMessage.Create(reply, timeProvider.GetUtcNow()));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_requests" })
        {
            // The original request and reply were committed together. A redelivery needs no new reply.
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }
}
