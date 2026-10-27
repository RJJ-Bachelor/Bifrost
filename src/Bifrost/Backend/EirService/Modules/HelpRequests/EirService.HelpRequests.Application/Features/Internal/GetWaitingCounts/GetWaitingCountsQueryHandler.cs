using EirService.Contracts.HelpRequests.GetWaitingCounts;
using EirService.HelpRequests.Application.Repositories;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Internal.GetWaitingCounts;

public sealed class GetWaitingCountsQueryHandler(IHelpRequestRepository repository)
    : ICommandHandler<GetWaitingCountsQuery, IReadOnlyDictionary<Guid, WaitingCounts>>
{
    public Task<IReadOnlyDictionary<Guid, WaitingCounts>> ExecuteAsync(
        GetWaitingCountsQuery query, CancellationToken cancellationToken) =>
        repository.GetWaitingCountsAsync(query.SessionGenerations, cancellationToken);
}
