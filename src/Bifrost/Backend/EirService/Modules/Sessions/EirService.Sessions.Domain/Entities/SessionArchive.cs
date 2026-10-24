using Shared.Domain.Primitives;

namespace EirService.Sessions.Domain.Entities;

public sealed class SessionArchive : Entity<Guid>
{
    private SessionArchive() { }

    public Guid SessionId { get; private set; }
    public string Title { get; private set; } = null!;
    public int Generation { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset ArchivedAtUtc { get; private set; }

    internal static SessionArchive Create(Guid sessionId, string title, int generation,
        DateTimeOffset startedAtUtc, DateTimeOffset archivedAtUtc) => new()
    {
        Id = Guid.NewGuid(), SessionId = sessionId, Title = title, Generation = generation,
        StartedAtUtc = startedAtUtc.ToUniversalTime(), ArchivedAtUtc = archivedAtUtc.ToUniversalTime()
    };
}
