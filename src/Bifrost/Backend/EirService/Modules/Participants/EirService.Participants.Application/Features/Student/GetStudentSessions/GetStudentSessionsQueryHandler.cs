using EirService.Contracts.Sessions.Common;
using EirService.Contracts.Sessions.GetSessionSummaries;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using FastEndpoints;

namespace EirService.Participants.Application.Features.Student.GetStudentSessions;

public sealed class GetStudentSessionsQueryHandler(IParticipantRepository repository)
    : ICommandHandler<GetStudentSessionsQuery, IReadOnlyList<SessionSummary>>
{
    public async Task<IReadOnlyList<SessionSummary>> ExecuteAsync(GetStudentSessionsQuery query, CancellationToken cancellationToken)
    {
        var ids = await repository.GetSessionIdsAsync(query.StudentId, ParticipantRole.Student, cancellationToken);
        return await new GetSessionSummariesQuery(ids).ExecuteAsync(cancellationToken);
    }
}
