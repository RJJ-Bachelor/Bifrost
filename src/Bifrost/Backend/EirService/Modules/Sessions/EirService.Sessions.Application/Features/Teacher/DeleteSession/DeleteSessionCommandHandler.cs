using EirService.Contracts.Exceptions;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;
using Shared.Application.Persistence;

namespace EirService.Sessions.Application.Features.Teacher.DeleteSession;

public sealed class DeleteSessionCommandHandler(
    ISessionRepository repository, IUnitOfWork<ISessionRepository> unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<DeleteSessionCommand>
{
    public async Task ExecuteAsync(DeleteSessionCommand command, CancellationToken cancellationToken)
    {
        var session = await repository.GetAsync(command.SessionId, cancellationToken);
        if (session is null || session.CreatedByTeacherId != command.TeacherId)
            throw new ResourceNotFoundException();

        session.Delete(timeProvider.GetUtcNow());
        repository.Remove(session);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UnitOfWorkConcurrencyException)
        {
            throw new OperationConflictException("The session was changed by another operation. Refresh before retrying.");
        }
    }
}
