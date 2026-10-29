using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using FastEndpoints;
using Shared.Application.Persistence;

namespace EirService.HelpRequests.Application.Features.Teacher.AnswerHelpRequest;

public sealed class AnswerHelpRequestCommandHandler(HelpRequestAccess access, IHelpRequestRepository repository, IUnitOfWork<IHelpRequestRepository> unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<AnswerHelpRequestCommand, HelpRequestResult>
{
    public async Task<HelpRequestResult> ExecuteAsync(AnswerHelpRequestCommand command, CancellationToken cancellationToken)
    {
        var request = await access.ForRequestAsync(command.Id, command.TeacherId, AuthorRole.Teacher, true, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var answer = new MessageText(command.Text);
        var question = command.Question is null ? null : new MessageText(command.Question);
        request.Answer(command.TeacherId, answer, now, question);
        if (request.IsFaq)
        {
            var linked = await repository.GetLinkedToFaqAsync(request.Id, cancellationToken);
            foreach (var source in linked.Where(source => source.Status == HelpRequestStatus.AwaitingFaq &&
                source.SessionId == request.SessionId && source.Generation == request.Generation))
                source.SuggestFaq(request.Id, command.TeacherId, answer, now, question);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await request.ToResultAsync(cancellationToken);
    }
}
