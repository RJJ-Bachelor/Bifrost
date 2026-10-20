using EirService.Sessions.Application.Repositories;
using FastEndpoints;
using EirService.Contracts.Exceptions;
using EirService.Contracts.Participants.IsSessionTeacher;
using Shared.Application.Persistence;

namespace EirService.Sessions.Application.Features.Teacher.ClearSession;

public sealed class ClearSessionCommandHandler(
    ISessionRepository repository, IUnitOfWork<ISessionRepository> unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<ClearSessionCommand, ClearSessionResult>
{
    public async Task<ClearSessionResult> ExecuteAsync(ClearSessionCommand command, CancellationToken cancellationToken)
    {
        var session = await repository.GetAsync(command.SessionId, cancellationToken);

        if (session is null || (session.CreatedByTeacherId != command.TeacherId &&
            !await new IsSessionTeacherQuery(session.Id, command.TeacherId).ExecuteAsync(cancellationToken)))
            throw new ResourceNotFoundException();

        if (session.CurrentGeneration != command.ExpectedGeneration)
            throw new OperationConflictException("The session has already been cleared. Refresh its generation before retrying.");

        var now = timeProvider.GetUtcNow();

        session.Clear(command.ExpectedGeneration, now);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UnitOfWorkConcurrencyException)
        {
            throw new OperationConflictException("The session was cleared by another operation.");
        }

        return new ClearSessionResult(session.Id, session.CurrentGeneration, now);
    }
}
