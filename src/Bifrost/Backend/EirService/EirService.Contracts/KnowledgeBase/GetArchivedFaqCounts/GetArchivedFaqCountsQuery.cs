using FastEndpoints;

namespace EirService.Contracts.KnowledgeBase.GetArchivedFaqCounts;

// Sessions authorizes the caller before requesting content from this module.
public sealed record GetArchivedFaqCountsQuery(Guid SessionId, int[] Generations, bool IncludeUnanswered)
    : ICommand<IReadOnlyDictionary<int, int>>;
