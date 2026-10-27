using EirService.Contracts.Participants.GetSessionTeachers;
using EirService.Participants.Application.Repositories;
using FastEndpoints;

namespace EirService.Participants.Application.Features.Internal.GetSessionTeachers;

public sealed class GetSessionTeachersQueryHandler(IParticipantRepository repository)
    : ICommandHandler<GetSessionTeachersQuery, IReadOnlyList<SessionTeacher>>
{
    public async Task<IReadOnlyList<SessionTeacher>> ExecuteAsync(GetSessionTeachersQuery query, CancellationToken cancellationToken) =>
        (await repository.GetTeachersAsync(query.SessionIds, cancellationToken))
            .Select(participant => new SessionTeacher(participant.SessionId, participant.SubjectId, participant.DisplayName)).ToArray();
}
