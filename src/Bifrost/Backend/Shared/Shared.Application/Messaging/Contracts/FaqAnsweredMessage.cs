namespace Shared.Application.Messaging.Contracts;

public sealed record FaqAnsweredMessage(
    Guid FaqId, Guid SessionId, int Generation, string Answer, string TeacherId, DateTimeOffset AnsweredAt);
