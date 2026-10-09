using GnaService.Domain.ValueObjects;

namespace GnaService.Domain.Entities;

public sealed class Notification
{
    private Notification()
    {
    }

    private Notification(
        string id,
        UserId userId,
        NotificationMessages messages)
    {
        Id = id;
        UserId = userId;
        Messages = messages;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string Id { get; private set; } = null!;

    public UserId UserId { get; private set; } = null!;

    public NotificationMessages Messages { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Notification Create(
        string id,
        string userId,
        string messages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return new Notification(
            id,
            UserId.Create(userId),
            NotificationMessages.Create(messages));
    }
}
