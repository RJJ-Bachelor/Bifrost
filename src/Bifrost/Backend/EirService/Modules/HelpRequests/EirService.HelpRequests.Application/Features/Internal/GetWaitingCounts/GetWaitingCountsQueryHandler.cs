using EirService.Contracts.HelpRequests.GetWaitingCounts;
using EirService.HelpRequests.Application.Repositories;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Internal.GetWaitingCounts;

public sealed class GetWaitingCountsQueryHandler(IHelpRequestRepository repository)
    : ICommandHandler<GetWaitingCountsQuery, IReadOnlyDictionary<Guid, int>>
{
    public Task<IReadOnlyDictionary<Guid, int>> ExecuteAsync(
        GetWaitingCountsQuery query, CancellationToken cancellationToken) =>
        repository.GetWaitingCountsAsync(query.SessionGenerations, cancellationToken);
}
