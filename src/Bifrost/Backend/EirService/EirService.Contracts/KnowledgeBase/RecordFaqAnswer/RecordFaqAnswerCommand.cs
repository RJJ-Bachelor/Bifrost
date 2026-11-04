using FastEndpoints;

namespace EirService.Contracts.KnowledgeBase.RecordFaqAnswer;

public sealed record RecordFaqAnswerCommand(
    Guid FaqId, Guid SessionId, int Generation, string Answer, string TeacherId,
    DateTimeOffset AnsweredAt, string? Question = null) : ICommand;
