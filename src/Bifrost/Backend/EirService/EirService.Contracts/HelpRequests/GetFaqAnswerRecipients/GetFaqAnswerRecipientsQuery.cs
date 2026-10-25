using FastEndpoints;

namespace EirService.Contracts.HelpRequests.GetFaqAnswerRecipients;

public sealed record GetFaqAnswerRecipientsQuery(Guid FaqId, Guid SessionId, int Generation)
    : ICommand<IReadOnlyList<FaqAnswerRecipient>>;

public sealed record FaqAnswerRecipient(string StudentId, Guid HelpRequestId, string Status);
