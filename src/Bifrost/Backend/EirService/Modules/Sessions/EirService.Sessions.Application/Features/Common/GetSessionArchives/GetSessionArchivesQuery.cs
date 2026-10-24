using FastEndpoints;

namespace EirService.Sessions.Application.Features.Common.GetSessionArchives;

public sealed record GetSessionArchivesQuery(Guid SessionId, string ActorId, bool IsTeacher) : ICommand<IReadOnlyList<SessionArchiveSummary>>;
