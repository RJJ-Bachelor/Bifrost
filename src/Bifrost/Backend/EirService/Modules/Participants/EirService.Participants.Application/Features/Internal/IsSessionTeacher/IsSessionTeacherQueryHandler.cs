using EirService.Contracts.Participants.IsSessionTeacher;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using FastEndpoints;

namespace EirService.Participants.Application.Features.Internal.IsSessionTeacher;

public sealed class IsSessionTeacherQueryHandler(IParticipantRepository repository)
    : ICommandHandler<IsSessionTeacherQuery, bool>
{
    public Task<bool> ExecuteAsync(IsSessionTeacherQuery query, CancellationToken cancellationToken) =>
        repository.IsParticipantAsync(query.SessionId, query.TeacherId, ParticipantRole.Teacher, cancellationToken);
}
