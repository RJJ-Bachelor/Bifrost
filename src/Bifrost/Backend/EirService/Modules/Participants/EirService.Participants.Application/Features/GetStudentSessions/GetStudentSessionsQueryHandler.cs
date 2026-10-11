using EirService.Contracts.Sessions.Common;
using EirService.Contracts.Sessions.GetSessionSummaries;
using EirService.Participants.Application.Repositories;
using FastEndpoints;

namespace EirService.Participants.Application.Features.GetStudentSessions;

public sealed class GetStudentSessionsQueryHandler(IParticipantRepository repository)
    : ICommandHandler<GetStudentSessionsQuery, IReadOnlyList<StudentSessionSummary>>
{
    public async Task<IReadOnlyList<StudentSessionSummary>> ExecuteAsync(GetStudentSessionsQuery query, CancellationToken cancellationToken)
    {
        var ids = await repository.GetSessionIdsAsync(query.StudentId, cancellationToken);
        return await new GetSessionSummariesQuery(ids).ExecuteAsync(cancellationToken);
    }
}
