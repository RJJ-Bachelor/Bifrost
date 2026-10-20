using EirService.Contracts.Sessions.Common;
using EirService.Contracts.Sessions.GetSessionSummaries;
using EirService.Sessions.Application.Features.Common;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.Internal.GetSessionSummaries;

public sealed class GetSessionSummariesQueryHandler(ISessionRepository repository)
    : ICommandHandler<GetSessionSummariesQuery, IReadOnlyList<SessionSummary>>
{
    public async Task<IReadOnlyList<SessionSummary>> ExecuteAsync(GetSessionSummariesQuery query, CancellationToken cancellationToken) =>
        (await repository.GetByIdsAsync(query.SessionIds, cancellationToken)).Select(session => session.ToSummary()).ToArray();
}
