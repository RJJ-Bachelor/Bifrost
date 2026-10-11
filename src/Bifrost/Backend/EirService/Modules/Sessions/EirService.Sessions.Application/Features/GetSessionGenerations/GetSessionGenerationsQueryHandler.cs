using EirService.Contracts.Sessions.GetSessionGenerations;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.GetSessionGenerations;

public sealed class GetSessionGenerationsQueryHandler(ISessionRepository repository)
    : ICommandHandler<GetSessionGenerationsQuery, IReadOnlyDictionary<Guid, int>>
{
    public async Task<IReadOnlyDictionary<Guid, int>> ExecuteAsync(GetSessionGenerationsQuery query, CancellationToken cancellationToken) =>
        (await repository.GetByIdsAsync(query.SessionIds, cancellationToken)).ToDictionary(session => session.Id, session => session.CurrentGeneration);
}
