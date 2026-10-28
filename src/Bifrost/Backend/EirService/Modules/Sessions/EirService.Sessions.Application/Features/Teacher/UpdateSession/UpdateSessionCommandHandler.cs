using EirService.Contracts.Exceptions;
using EirService.Contracts.Participants.IsSessionTeacher;
using EirService.Sessions.Application.Features.Teacher.Common;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;
using Shared.Application.Persistence;

namespace EirService.Sessions.Application.Features.Teacher.UpdateSession;

public sealed class UpdateSessionCommandHandler(
    ISessionRepository repository, IUnitOfWork<ISessionRepository> unitOfWork)
    : ICommandHandler<UpdateSessionCommand, SessionResult>
{
    public async Task<SessionResult> ExecuteAsync(UpdateSessionCommand command, CancellationToken cancellationToken)
    {
        var session = await repository.GetAsync(command.SessionId, cancellationToken);
        if (session is null || (session.CreatedByTeacherId != command.TeacherId &&
            !await new IsSessionTeacherQuery(session.Id, command.TeacherId).ExecuteAsync(cancellationToken)))
            throw new ResourceNotFoundException();

        session.Rename(command.Title);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UnitOfWorkConcurrencyException)
        {
            throw new OperationConflictException("The session was changed by another operation. Refresh before retrying.");
        }

        return SessionResult.From(session);
    }
}
