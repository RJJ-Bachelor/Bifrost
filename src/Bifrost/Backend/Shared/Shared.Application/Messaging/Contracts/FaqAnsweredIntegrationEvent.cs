namespace Shared.Application.Messaging.Contracts;

[MessageContract("faq.answered")]
public sealed record FaqAnsweredIntegrationEvent(
    Guid FaqId, Guid SessionId, int Generation, string Answer, string TeacherId,
    DateTimeOffset AnsweredAt, string? Question = null);
