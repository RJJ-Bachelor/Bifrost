using EirService.Sessions.Domain.ValueObjects;
using EirService.Sessions.Domain.DomainEvents;
using Shared.Domain.Primitives;

namespace EirService.Sessions.Domain.Entities;

public sealed class Session : AggregateRoot<Guid>
{
    private readonly List<SessionArchive> archives = [];
    private Session() { }

    public string Title { get; private set; } = null!;
    public SessionCode Code { get; private set; } = null!;
    public SessionCode TeacherCode { get; private set; } = null!;
    public string CreatedByTeacherId { get; private set; } = null!;
    public string? CreatedByDisplayName { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public int CurrentGeneration { get; private set; }
    public DateTimeOffset CurrentGenerationStartedAtUtc { get; private set; }
    public DateTimeOffset? LastClearedAtUtc { get; private set; }
    public IReadOnlyCollection<SessionArchive> Archives => archives.AsReadOnly();

    public static Session Create(string title, SessionCode code, SessionCode teacherCode, string createdByTeacherId,
        string? createdByDisplayName, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdByTeacherId);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(teacherCode);
        if (code == teacherCode)
            throw new ArgumentException("Student and teacher codes must be different.", nameof(teacherCode));

        var trimmedTitle = title.Trim();
        
        if (trimmedTitle.Length > 200)
            throw new ArgumentException("A session title cannot exceed 200 characters.", nameof(title));

        return new Session
        {
            Id = Guid.NewGuid(), Title = trimmedTitle, Code = code, TeacherCode = teacherCode,
            CreatedByTeacherId = createdByTeacherId,
            CreatedByDisplayName = createdByDisplayName,
            CreatedAtUtc = now.ToUniversalTime(), CurrentGeneration = 1,
            CurrentGenerationStartedAtUtc = now.ToUniversalTime()
        };
    }

    public void Delete(DateTimeOffset now) =>
        RaiseDomainEvent(new SessionDeletedDomainEvent(Id, CurrentGeneration, now.ToUniversalTime()));

    public void Clear(int expectedGeneration, DateTimeOffset now)
    {
        if (CurrentGeneration != expectedGeneration)
            throw new InvalidOperationException("The session generation has changed.");

        var previousGeneration = CurrentGeneration;
        CurrentGeneration = checked(CurrentGeneration + 1);
        LastClearedAtUtc = now.ToUniversalTime();
        CurrentGenerationStartedAtUtc = LastClearedAtUtc.Value;
        RaiseDomainEvent(new SessionClearedDomainEvent(Id, previousGeneration, CurrentGeneration, LastClearedAtUtc.Value));
    }

    public SessionArchive Archive(int expectedGeneration, DateTimeOffset now)
    {
        if (CurrentGeneration != expectedGeneration)
            throw new InvalidOperationException("The session generation has changed.");

        var nextGeneration = checked(CurrentGeneration + 1);
        var archive = SessionArchive.Create(Id, Title, CurrentGeneration, CurrentGenerationStartedAtUtc, now);
        archives.Add(archive);
        CurrentGeneration = nextGeneration;
        CurrentGenerationStartedAtUtc = archive.ArchivedAtUtc;
        RaiseDomainEvent(new SessionArchivedDomainEvent(Id, archive.Generation, CurrentGeneration, archive.ArchivedAtUtc));
        return archive;
    }
}
