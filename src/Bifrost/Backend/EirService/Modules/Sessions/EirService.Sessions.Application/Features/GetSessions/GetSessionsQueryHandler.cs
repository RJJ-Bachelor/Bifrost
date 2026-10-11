using EirService.Sessions.Application.Features.Common;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.GetSessions;

public sealed class GetSessionsQueryHandler(ISessionRepository repository)
    : ICommandHandler<GetSessionsQuery, IReadOnlyList<SessionResult>>
{
    public async Task<IReadOnlyList<SessionResult>> ExecuteAsync(GetSessionsQuery query, CancellationToken cancellationToken) =>
        (await repository.GetByTeacherAsync(query.TeacherId, cancellationToken)).Select(SessionResult.From).ToArray();
}
