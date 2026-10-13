using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Teacher.TakeNextHelpRequest;

public sealed class TakeNextHelpRequestCommandHandler(IHelpRequestRepository repository, HelpRequestAccess access)
    : ICommandHandler<TakeNextHelpRequestCommand, HelpRequestResult?>
{
    public async Task<HelpRequestResult?> ExecuteAsync(TakeNextHelpRequestCommand command, CancellationToken cancellationToken)
    {
        var session = await access.ForSessionAsync(command.SessionId, command.TeacherId, AuthorRole.Teacher, cancellationToken);
        var request = await repository.TakeNextAsync(session.Id, session.Generation, command.TeacherId, cancellationToken);
        return request?.ToResult();
    }
}
