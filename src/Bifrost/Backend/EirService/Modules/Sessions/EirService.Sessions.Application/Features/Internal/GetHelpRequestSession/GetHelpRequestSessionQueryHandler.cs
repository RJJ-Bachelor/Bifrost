using EirService.Contracts.Sessions.GetHelpRequestSession;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.Internal.GetHelpRequestSession;

public sealed class GetHelpRequestSessionQueryHandler(ISessionRepository repository)
    : ICommandHandler<GetHelpRequestSessionQuery, HelpRequestSession?>
{
    public async Task<HelpRequestSession?> ExecuteAsync(GetHelpRequestSessionQuery query, CancellationToken cancellationToken)
    {
        var session = await repository.GetAsync(query.SessionId, cancellationToken);
        return session is null ? null : new HelpRequestSession(session.Id, session.CurrentGeneration, session.CreatedByTeacherId);
    }
}
