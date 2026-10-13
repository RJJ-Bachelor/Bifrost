using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using FastEndpoints;
using Shared.Application.Persistence;

namespace EirService.HelpRequests.Application.Features.Teacher.AnswerHelpRequest;

public sealed class AnswerHelpRequestCommandHandler(HelpRequestAccess access, IUnitOfWork<IHelpRequestRepository> unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<AnswerHelpRequestCommand, HelpRequestResult>
{
    public async Task<HelpRequestResult> ExecuteAsync(AnswerHelpRequestCommand command, CancellationToken cancellationToken)
    {
        var request = await access.ForRequestAsync(command.Id, command.TeacherId, AuthorRole.Teacher, true, cancellationToken);
        var now = timeProvider.GetUtcNow();
        request.Answer(command.TeacherId, new MessageText(command.Text), now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return request.ToResult();
    }
}
