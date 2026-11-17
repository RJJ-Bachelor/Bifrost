namespace Shared.Application.Messaging.Contracts;

[MessageContract("faq.question-changed")]
public sealed record FaqQuestionChangedIntegrationEvent(
    Guid FaqId, Guid SessionId, int Generation, string PreviousQuestion, string Question,
    string Answer, string TeacherId, DateTimeOffset ChangedAtUtc);
