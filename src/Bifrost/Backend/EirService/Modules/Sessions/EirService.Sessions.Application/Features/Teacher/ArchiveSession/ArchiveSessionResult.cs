namespace EirService.Sessions.Application.Features.Teacher.ArchiveSession;

public sealed record ArchiveSessionResult(Guid ArchiveId, Guid SessionId, string Title, int Generation,
    int CurrentGeneration, DateTimeOffset StartedAtUtc, DateTimeOffset ArchivedAtUtc);
