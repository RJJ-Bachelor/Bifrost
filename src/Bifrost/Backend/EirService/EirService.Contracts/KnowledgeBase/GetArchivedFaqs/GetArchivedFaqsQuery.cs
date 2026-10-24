using FastEndpoints;

namespace EirService.Contracts.KnowledgeBase.GetArchivedFaqs;

// Sessions authorizes the caller before requesting content from this module.
public sealed record GetArchivedFaqsQuery(Guid SessionId, int Generation, bool IncludeUnanswered)
    : ICommand<IReadOnlyList<ArchivedFaq>>;

public sealed record ArchivedFaq(
    Guid Id, Guid SessionId, int Generation, string Question, string? Answer,
    int SourceCount, DateTimeOffset CreatedAt, DateTimeOffset? AnsweredAt);
