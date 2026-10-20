using EirService.Contracts.Participants.GetTeacherSessionIds;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using FastEndpoints;

namespace EirService.Participants.Application.Features.Internal.GetTeacherSessionIds;

public sealed class GetTeacherSessionIdsQueryHandler(IParticipantRepository repository)
    : ICommandHandler<GetTeacherSessionIdsQuery, Guid[]>
{
    public Task<Guid[]> ExecuteAsync(GetTeacherSessionIdsQuery query, CancellationToken cancellationToken) =>
        repository.GetSessionIdsAsync(query.TeacherId, ParticipantRole.Teacher, cancellationToken);
}
