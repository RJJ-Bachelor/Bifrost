using EirService.Contracts.Participants.GetSessionTeacherIds;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using FastEndpoints;

namespace EirService.Participants.Application.Features.Internal.GetSessionTeacherIds;

public sealed class GetSessionTeacherIdsQueryHandler(IParticipantRepository repository)
    : ICommandHandler<GetSessionTeacherIdsQuery, string[]>
{
    public Task<string[]> ExecuteAsync(GetSessionTeacherIdsQuery query, CancellationToken cancellationToken) =>
        repository.GetSubjectIdsAsync(query.SessionId, ParticipantRole.Teacher, cancellationToken);
}
