using EirService.Contracts.Exceptions;
using EirService.Contracts.Participants.IsSessionTeacher;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;
using Shared.Application.Persistence;

namespace EirService.Sessions.Application.Features.Teacher.ArchiveSession;

public sealed class ArchiveSessionCommandHandler(
    ISessionRepository repository, IUnitOfWork<ISessionRepository> unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<ArchiveSessionCommand, ArchiveSessionResult>
{
    public async Task<ArchiveSessionResult> ExecuteAsync(ArchiveSessionCommand command, CancellationToken cancellationToken)
    {
        var session = await repository.GetAsync(command.SessionId, cancellationToken);
        if (session is null || (session.CreatedByTeacherId != command.TeacherId &&
            !await new IsSessionTeacherQuery(session.Id, command.TeacherId).ExecuteAsync(cancellationToken)))
            throw new ResourceNotFoundException();

        if (session.CurrentGeneration != command.ExpectedGeneration)
            throw new OperationConflictException("The session generation has changed. Refresh before archiving.");

        var archive = session.Archive(command.ExpectedGeneration, timeProvider.GetUtcNow(), command.Title);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UnitOfWorkConcurrencyException)
        {
            throw new OperationConflictException("The session was changed by another operation.");
        }

        return new ArchiveSessionResult(archive.Id, session.Id, archive.Title, archive.Generation,
            session.CurrentGeneration, archive.StartedAtUtc, archive.ArchivedAtUtc);
    }
}
