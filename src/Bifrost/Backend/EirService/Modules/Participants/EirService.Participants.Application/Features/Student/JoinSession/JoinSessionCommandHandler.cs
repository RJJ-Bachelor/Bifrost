using EirService.Contracts.Sessions.Common;
using EirService.Contracts.Sessions.FindSessionByCode;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using FastEndpoints;
using EirService.Contracts.Exceptions;

namespace EirService.Participants.Application.Features.Student.JoinSession;

public sealed class JoinSessionCommandHandler(IParticipantRepository repository, TimeProvider timeProvider)
    : ICommandHandler<JoinSessionCommand, StudentSessionSummary>
{
    public async Task<StudentSessionSummary> ExecuteAsync(JoinSessionCommand command, CancellationToken cancellationToken)
    {
        var session = await new FindSessionByCodeQuery(command.Code).ExecuteAsync(cancellationToken)
            ?? throw new ResourceNotFoundException();
        await repository.JoinAsync(SessionParticipant.Join(session.Id, command.StudentId, ParticipantRole.Student, timeProvider.GetUtcNow()), cancellationToken);
        return session;
    }
}
