using FastEndpoints;

namespace EirService.Contracts.HelpRequests.GetArchivedConversationCounts;

// Sessions authorizes the caller before requesting content from this module.
public sealed record GetArchivedConversationCountsQuery(Guid SessionId, int[] Generations, string? StudentId)
    : ICommand<IReadOnlyDictionary<int, int>>;
