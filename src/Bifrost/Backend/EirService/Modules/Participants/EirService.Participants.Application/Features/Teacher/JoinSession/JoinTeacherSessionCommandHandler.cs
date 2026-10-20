using EirService.Contracts.Exceptions;
using EirService.Contracts.Sessions.FindSessionByCode;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using FastEndpoints;

namespace EirService.Participants.Application.Features.Teacher.JoinSession;

public sealed class JoinTeacherSessionCommandHandler(IParticipantRepository repository, TimeProvider timeProvider)
    : ICommandHandler<JoinTeacherSessionCommand, Guid>
{
    public async Task<Guid> ExecuteAsync(JoinTeacherSessionCommand command, CancellationToken cancellationToken)
    {
        var session = await new FindSessionByCodeQuery(command.Code, SessionCodeType.Teacher).ExecuteAsync(cancellationToken)
            ?? throw new ResourceNotFoundException();
        await repository.JoinAsync(SessionParticipant.Join(session.Id, command.TeacherId,
            ParticipantRole.Teacher, timeProvider.GetUtcNow()), cancellationToken);
        return session.Id;
    }
}
