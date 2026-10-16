using EirService.Contracts.Sessions.GetSessionGenerations;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace EirService.Sessions.Application.Features.Internal.GetSessionGenerations;

public sealed class GetSessionGenerationsQueryHandler(IServiceScopeFactory scopeFactory)
    : ICommandHandler<GetSessionGenerationsQuery, IReadOnlyDictionary<Guid, int>>
{
    public async Task<IReadOnlyDictionary<Guid, int>> ExecuteAsync(GetSessionGenerationsQuery query, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        return (await repository.GetByIdsAsync(query.SessionIds, cancellationToken))
            .ToDictionary(session => session.Id, session => session.CurrentGeneration);
    }
}
