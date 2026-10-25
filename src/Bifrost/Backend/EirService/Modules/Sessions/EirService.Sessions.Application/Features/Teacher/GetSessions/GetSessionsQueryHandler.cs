using EirService.Contracts.HelpRequests.GetWaitingCounts;
using EirService.Contracts.Participants.GetTeacherSessionIds;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.Teacher.GetSessions;

public sealed class GetSessionsQueryHandler(ISessionRepository repository)
    : ICommandHandler<GetSessionsQuery, IReadOnlyList<SessionListResult>>
{
    public async Task<IReadOnlyList<SessionListResult>> ExecuteAsync(GetSessionsQuery query, CancellationToken cancellationToken)
    {
        var owned = await repository.GetByTeacherAsync(query.TeacherId, cancellationToken);
        var joinedIds = await new GetTeacherSessionIdsQuery(query.TeacherId).ExecuteAsync(cancellationToken);
        var joined = await repository.GetByIdsAsync(joinedIds, cancellationToken);
        var sessions = owned.Concat(joined).DistinctBy(session => session.Id)
            .OrderByDescending(session => session.CreatedAtUtc).ThenBy(session => session.Id)
            .ToArray();
        if (sessions.Length == 0) return [];

        var counts = await new GetWaitingCountsQuery(sessions.ToDictionary(session => session.Id, session => session.CurrentGeneration))
            .ExecuteAsync(cancellationToken);
        return sessions.Select(session => SessionListResult.From(session, counts.GetValueOrDefault(session.Id))).ToArray();
    }
}
