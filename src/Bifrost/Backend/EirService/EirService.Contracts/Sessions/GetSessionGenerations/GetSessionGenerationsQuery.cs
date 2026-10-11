using FastEndpoints;

namespace EirService.Contracts.Sessions.GetSessionGenerations;

public sealed record GetSessionGenerationsQuery(Guid[] SessionIds) : ICommand<IReadOnlyDictionary<Guid, int>>;
