using EirService.Contracts.Sessions.Common;
using EirService.Contracts.Sessions.GetSessionSummaries;
using EirService.Sessions.Application.Features.Common;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.GetSessionSummaries;

public sealed class GetSessionSummariesQueryHandler(ISessionRepository repository)
    : ICommandHandler<GetSessionSummariesQuery, IReadOnlyList<StudentSessionSummary>>
{
    public async Task<IReadOnlyList<StudentSessionSummary>> ExecuteAsync(GetSessionSummariesQuery query, CancellationToken cancellationToken) =>
        (await repository.GetByIdsAsync(query.SessionIds, cancellationToken)).Select(session => session.ToStudentSummary()).ToArray();
}
