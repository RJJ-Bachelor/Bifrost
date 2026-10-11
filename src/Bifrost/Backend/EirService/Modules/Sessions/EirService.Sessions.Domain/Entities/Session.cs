using EirService.Sessions.Domain.ValueObjects;

namespace EirService.Sessions.Domain.Entities;

public sealed class Session
{
    private Session() { }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = null!;
    public SessionCode Code { get; private set; } = null!;
    public string CreatedBySubjectId { get; private set; } = null!;
    public string? CreatedByDisplayName { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public int CurrentGeneration { get; private set; }
    public DateTimeOffset? LastClearedAtUtc { get; private set; }

    public static Session Create(string title, SessionCode code, string createdBySubjectId,
        string? createdByDisplayName, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBySubjectId);
        ArgumentNullException.ThrowIfNull(code);
        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length > 200)
            throw new ArgumentException("A session title cannot exceed 200 characters.", nameof(title));

        return new Session
        {
            Id = Guid.NewGuid(), Title = trimmedTitle, Code = code,
            CreatedBySubjectId = createdBySubjectId,
            CreatedByDisplayName = createdByDisplayName,
            CreatedAtUtc = now.ToUniversalTime(), CurrentGeneration = 1
        };
    }

    public void Clear(int expectedGeneration, DateTimeOffset now)
    {
        if (CurrentGeneration != expectedGeneration)
            throw new InvalidOperationException("The session generation has changed.");

        CurrentGeneration = checked(CurrentGeneration + 1);
        LastClearedAtUtc = now.ToUniversalTime();
    }
}
