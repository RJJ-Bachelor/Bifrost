using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using FastEndpoints;
using Shared.Application.Persistence;

namespace EirService.HelpRequests.Application.Features.Student.AddStudentMessage;

public sealed class AddStudentMessageCommandHandler(HelpRequestAccess access, IUnitOfWork<IHelpRequestRepository> unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<AddStudentMessageCommand, HelpRequestResult>
{
    public async Task<HelpRequestResult> ExecuteAsync(AddStudentMessageCommand command, CancellationToken cancellationToken)
    {
        var request = await access.ForRequestAsync(command.Id, command.StudentId, AuthorRole.Student, true, cancellationToken);
        var now = timeProvider.GetUtcNow();
        request.AddStudentMessage(command.StudentId, new MessageText(command.Text), now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return request.ToResult();
    }
}
