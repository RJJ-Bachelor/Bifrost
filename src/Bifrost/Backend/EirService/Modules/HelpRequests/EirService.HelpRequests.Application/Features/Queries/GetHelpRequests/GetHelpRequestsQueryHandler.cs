using EirService.HelpRequests.Application.Repositories;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Queries.GetHelpRequests;

public sealed class GetHelpRequestsQueryHandler(IRequestRepository repository)
    : ICommandHandler<GetHelpRequestsQuery, IReadOnlyList<HelpRequestResult>>
{
    public async Task<IReadOnlyList<HelpRequestResult>> ExecuteAsync(
        GetHelpRequestsQuery query, CancellationToken cancellationToken)
    {
        var requests = await repository.GetByUserIdAsync(query.UserId, cancellationToken);
        return requests.Select(request => new HelpRequestResult(request.Id, request.UserId, request.Message)).ToArray();
    }
}
