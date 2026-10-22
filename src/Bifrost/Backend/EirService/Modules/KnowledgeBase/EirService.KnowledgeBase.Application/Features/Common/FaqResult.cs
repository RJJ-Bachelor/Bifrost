using EirService.KnowledgeBase.Domain.Entities;

namespace EirService.KnowledgeBase.Application.Features.Common;

public sealed record FaqResult(Guid Id, Guid SessionId, int Generation, string Question, string? Answer,
    int SourceCount, DateTimeOffset CreatedAt, DateTimeOffset? AnsweredAt);

public static class FaqMapping
{
    public static FaqResult ToResult(this FaqAnswer faq) => new(faq.Id, faq.SessionId, faq.Generation,
        faq.Question, faq.Answer, faq.HelpRequestIds.Count, faq.CreatedAt, faq.AnsweredAt);
}
