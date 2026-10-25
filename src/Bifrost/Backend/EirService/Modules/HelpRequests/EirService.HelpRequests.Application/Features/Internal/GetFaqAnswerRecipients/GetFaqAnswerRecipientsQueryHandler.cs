using EirService.Contracts.HelpRequests.GetFaqAnswerRecipients;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Internal.GetFaqAnswerRecipients;

public sealed class GetFaqAnswerRecipientsQueryHandler(IHelpRequestRepository repository)
    : ICommandHandler<GetFaqAnswerRecipientsQuery, IReadOnlyList<FaqAnswerRecipient>>
{
    public async Task<IReadOnlyList<FaqAnswerRecipient>> ExecuteAsync(
        GetFaqAnswerRecipientsQuery query, CancellationToken cancellationToken)
    {
        var requests = await repository.GetLinkedToFaqAsync(query.FaqId, cancellationToken);
        return requests.Where(request => request.SessionId == query.SessionId && request.Generation == query.Generation &&
                request.StudentId is not null && request.Status == HelpRequestStatus.AwaitingConfirmation)
            .Select(request => new FaqAnswerRecipient(request.StudentId!, request.Id, request.Status.ToString())).ToArray();
    }
}
