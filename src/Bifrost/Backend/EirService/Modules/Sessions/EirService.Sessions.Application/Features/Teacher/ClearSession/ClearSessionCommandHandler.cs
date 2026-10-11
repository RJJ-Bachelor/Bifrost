using EirService.Sessions.Application.Repositories;
using FastEndpoints;
using EirService.Contracts.Exceptions;

namespace EirService.Sessions.Application.Features.Teacher.ClearSession;

public sealed class ClearSessionCommandHandler(ISessionRepository repository, TimeProvider timeProvider)
    : ICommandHandler<ClearSessionCommand, ClearSessionResult>
{
    public async Task<ClearSessionResult> ExecuteAsync(ClearSessionCommand command, CancellationToken cancellationToken)
    {
        var session = await repository.GetAsync(command.SessionId, cancellationToken);

        if (session is null || session.CreatedByTeacherId != command.TeacherId)
            throw new ResourceNotFoundException();

        if (session.CurrentGeneration != command.ExpectedGeneration)
            throw new OperationConflictException("The session has already been cleared. Refresh its generation before retrying.");

        var now = timeProvider.GetUtcNow();

        session.Clear(command.ExpectedGeneration, now);
        
        if (!await repository.TryClearAsync(session, cancellationToken))
            throw new OperationConflictException("The session was cleared by another operation.");

        return new ClearSessionResult(session.Id, session.CurrentGeneration, now);
    }
}
