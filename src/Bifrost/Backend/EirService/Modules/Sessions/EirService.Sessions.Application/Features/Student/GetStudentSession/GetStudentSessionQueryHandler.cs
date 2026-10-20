using EirService.Contracts.Participants.IsSessionParticipant;
using EirService.Contracts.Sessions.Common;
using EirService.Sessions.Application.Features.Common;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;
using EirService.Contracts.Exceptions;

namespace EirService.Sessions.Application.Features.Student.GetStudentSession;

public sealed class GetStudentSessionQueryHandler(ISessionRepository repository)
    : ICommandHandler<GetStudentSessionQuery, SessionSummary>
{
    public async Task<SessionSummary> ExecuteAsync(GetStudentSessionQuery query, CancellationToken cancellationToken)
    {
        if (!await new IsSessionParticipantQuery(query.SessionId, query.StudentId).ExecuteAsync(cancellationToken))
            throw new ResourceNotFoundException();

        var session = await repository.GetAsync(query.SessionId, cancellationToken) ?? throw new ResourceNotFoundException();
        return session.ToSummary();
    }
}
