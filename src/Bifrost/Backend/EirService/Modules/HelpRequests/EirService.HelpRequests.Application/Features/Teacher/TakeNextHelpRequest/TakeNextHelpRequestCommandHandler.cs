using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;
using EirService.Contracts.Participants.RememberSessionTeacher;

namespace EirService.HelpRequests.Application.Features.Teacher.TakeNextHelpRequest;

public sealed class TakeNextHelpRequestCommandHandler(IHelpRequestRepository repository, HelpRequestAccess access)
    : ICommandHandler<TakeNextHelpRequestCommand, HelpRequestResult?>
{
    public async Task<HelpRequestResult?> ExecuteAsync(TakeNextHelpRequestCommand command, CancellationToken cancellationToken)
    {
        var session = await access.ForSessionAsync(command.SessionId, command.TeacherId, AuthorRole.Teacher, cancellationToken);
        if (!string.IsNullOrWhiteSpace(command.DisplayName))
            await new RememberSessionTeacherCommand(session.Id, command.TeacherId, command.DisplayName).ExecuteAsync(cancellationToken);
        var request = await repository.TakeNextAsync(session.Id, session.Generation, command.TeacherId, cancellationToken);
        return request is null ? null : await request.ToResultAsync(cancellationToken);
    }
}
