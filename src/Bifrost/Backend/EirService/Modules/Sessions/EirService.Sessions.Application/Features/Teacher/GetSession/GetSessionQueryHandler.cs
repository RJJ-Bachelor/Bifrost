using EirService.Sessions.Application.Features.Teacher.Common;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;
using EirService.Contracts.Exceptions;

namespace EirService.Sessions.Application.Features.Teacher.GetSession;

public sealed class GetSessionQueryHandler(ISessionRepository repository) : ICommandHandler<GetSessionQuery, SessionResult>
{
    public async Task<SessionResult> ExecuteAsync(GetSessionQuery query, CancellationToken cancellationToken)
    {
        var session = await repository.GetAsync(query.SessionId, cancellationToken);
        if (session is null || session.CreatedByTeacherId != query.TeacherId)
            throw new ResourceNotFoundException();

        return SessionResult.From(session);
    }
}
