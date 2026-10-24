using EirService.Contracts.HelpRequests.GetArchivedConversationCounts;
using EirService.HelpRequests.Application.Repositories;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Internal.GetArchivedConversationCounts;

public sealed class GetArchivedConversationCountsQueryHandler(IArchivedConversationRepository repository)
    : ICommandHandler<GetArchivedConversationCountsQuery, IReadOnlyDictionary<int, int>>
{
    public Task<IReadOnlyDictionary<int, int>> ExecuteAsync(
        GetArchivedConversationCountsQuery query, CancellationToken cancellationToken) =>
        repository.CountForGenerationsAsync(query.SessionId, query.Generations, query.StudentId, cancellationToken);
}
