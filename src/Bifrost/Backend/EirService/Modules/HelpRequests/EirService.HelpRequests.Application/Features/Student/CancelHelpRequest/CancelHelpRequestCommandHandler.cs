using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;
using Shared.Application.Persistence;

namespace EirService.HelpRequests.Application.Features.Student.CancelHelpRequest;

public sealed class CancelHelpRequestCommandHandler(
    HelpRequestAccess access, IUnitOfWork<IHelpRequestRepository> unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<CancelHelpRequestCommand, HelpRequestResult>
{
    public async Task<HelpRequestResult> ExecuteAsync(CancelHelpRequestCommand command, CancellationToken cancellationToken)
    {
        var request = await access.ForRequestAsync(command.Id, command.StudentId, AuthorRole.Student, true, cancellationToken);
        request.Cancel(command.StudentId, AuthorRole.Student, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await request.ToResultAsync(cancellationToken);
    }
}
