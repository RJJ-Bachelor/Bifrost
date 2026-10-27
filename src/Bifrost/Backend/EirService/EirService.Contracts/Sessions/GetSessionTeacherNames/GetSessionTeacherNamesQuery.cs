using FastEndpoints;

namespace EirService.Contracts.Sessions.GetSessionTeacherNames;

public sealed record GetSessionTeacherNamesQuery(Guid[] SessionIds)
    : ICommand<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string?>>>;
