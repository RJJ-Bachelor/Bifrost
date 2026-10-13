using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;
using Shared.Application.Persistence;

namespace EirService.HelpRequests.Application.Features.Teacher.ChangeHelpRequestStatus;

public sealed class ChangeHelpRequestStatusCommandHandler(
    HelpRequestAccess access, IUnitOfWork<IHelpRequestRepository> unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<ChangeHelpRequestStatusCommand, HelpRequestResult>
{
    public async Task<HelpRequestResult> ExecuteAsync(ChangeHelpRequestStatusCommand command, CancellationToken cancellationToken)
    {
        var request = await access.ForRequestAsync(command.Id, command.TeacherId, AuthorRole.Teacher, true, cancellationToken);
        var now = timeProvider.GetUtcNow();
        switch (command.Action)
        {
            case HelpRequestAction.Start:
                request.Start(command.TeacherId, now);
                break;
            case HelpRequestAction.Complete:
                request.Complete(command.TeacherId, now);
                break;
            case HelpRequestAction.Cancel:
                request.Cancel(command.TeacherId, AuthorRole.Teacher, now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command.Action));
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return request.ToResult();
    }
}
