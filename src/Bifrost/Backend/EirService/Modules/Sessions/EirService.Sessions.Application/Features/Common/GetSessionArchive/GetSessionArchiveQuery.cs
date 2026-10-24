using FastEndpoints;

namespace EirService.Sessions.Application.Features.Common.GetSessionArchive;

public sealed record GetSessionArchiveQuery(Guid SessionId, Guid ArchiveId, string ActorId, bool IsTeacher) : ICommand<SessionArchiveResult>;
