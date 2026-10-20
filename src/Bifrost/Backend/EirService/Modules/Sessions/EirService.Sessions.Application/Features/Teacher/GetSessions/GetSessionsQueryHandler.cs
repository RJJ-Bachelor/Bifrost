using EirService.Sessions.Application.Features.Teacher.Common;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;
using EirService.Contracts.Participants.GetTeacherSessionIds;

namespace EirService.Sessions.Application.Features.Teacher.GetSessions;

public sealed class GetSessionsQueryHandler(ISessionRepository repository)
    : ICommandHandler<GetSessionsQuery, IReadOnlyList<SessionResult>>
{
    public async Task<IReadOnlyList<SessionResult>> ExecuteAsync(GetSessionsQuery query, CancellationToken cancellationToken)
    {
        var owned = await repository.GetByTeacherAsync(query.TeacherId, cancellationToken);
        var joinedIds = await new GetTeacherSessionIdsQuery(query.TeacherId).ExecuteAsync(cancellationToken);
        var joined = await repository.GetByIdsAsync(joinedIds, cancellationToken);
        return owned.Concat(joined).DistinctBy(session => session.Id)
            .OrderByDescending(session => session.CreatedAtUtc).ThenBy(session => session.Id)
            .Select(SessionResult.From).ToArray();
    }
}
