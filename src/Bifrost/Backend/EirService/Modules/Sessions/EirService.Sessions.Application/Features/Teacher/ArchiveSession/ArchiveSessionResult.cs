namespace EirService.Sessions.Application.Features.Teacher.ArchiveSession;

public sealed record ArchiveSessionResult(Guid ArchiveId, Guid SessionId, int Generation,
    int CurrentGeneration, DateTimeOffset ArchivedAtUtc);
