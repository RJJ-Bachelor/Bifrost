using FastEndpoints;

namespace EirService.Contracts.HelpRequests.GetWaitingCounts;

// Sessions supplies only the sessions visible to the current teacher.
public sealed record GetWaitingCountsQuery(IReadOnlyDictionary<Guid, int> SessionGenerations)
    : ICommand<IReadOnlyDictionary<Guid, WaitingCounts>>;
