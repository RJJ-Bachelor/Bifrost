using EirService.Contracts.KnowledgeBase.GetArchivedFaqCounts;
using EirService.KnowledgeBase.Application.Repositories;
using FastEndpoints;

namespace EirService.KnowledgeBase.Application.Features.Internal.GetArchivedFaqCounts;

public sealed class GetArchivedFaqCountsQueryHandler(IFaqRepository repository)
    : ICommandHandler<GetArchivedFaqCountsQuery, IReadOnlyDictionary<int, int>>
{
    public Task<IReadOnlyDictionary<int, int>> ExecuteAsync(GetArchivedFaqCountsQuery query, CancellationToken cancellationToken) =>
        repository.CountForGenerationsAsync(query.SessionId, query.Generations, query.IncludeUnanswered, cancellationToken);
}
