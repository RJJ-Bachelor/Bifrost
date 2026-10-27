using EirService.Contracts.Participants.RememberSessionTeacher;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using FastEndpoints;

namespace EirService.Participants.Application.Features.Internal.RememberSessionTeacher;

public sealed class RememberSessionTeacherCommandHandler(IParticipantRepository repository, TimeProvider timeProvider)
    : ICommandHandler<RememberSessionTeacherCommand>
{
    public Task ExecuteAsync(RememberSessionTeacherCommand command, CancellationToken cancellationToken) =>
        repository.JoinAsync(SessionParticipant.Join(command.SessionId, command.TeacherId, ParticipantRole.Teacher,
            timeProvider.GetUtcNow(), command.DisplayName), cancellationToken);
}
