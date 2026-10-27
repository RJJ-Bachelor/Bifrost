using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;
using Shared.Application.Persistence;

namespace EirService.HelpRequests.Application.Features.Student.RespondToFaq;

public sealed class RespondToFaqCommandHandler(HelpRequestAccess access, IUnitOfWork<IHelpRequestRepository> unitOfWork, TimeProvider clock)
    : ICommandHandler<RespondToFaqCommand, HelpRequestResult>
{
    public async Task<HelpRequestResult> ExecuteAsync(RespondToFaqCommand command, CancellationToken cancellationToken)
    {
        var request = await access.ForRequestAsync(command.Id, command.StudentId, AuthorRole.Student, true, cancellationToken);
        if (command.Accept!.Value) request.AcceptFaq(command.StudentId, clock.GetUtcNow());
        else request.RejectFaq(command.StudentId, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await request.ToResultAsync(cancellationToken);
    }
}
