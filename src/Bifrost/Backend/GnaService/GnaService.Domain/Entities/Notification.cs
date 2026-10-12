using Shared.Domain.Primitives;
using GnaService.Domain.ValueObjects;

namespace GnaService.Domain.Entities;

public sealed class Notification : AggregateRoot<Guid>
{
    private Notification()
    {
    }

    private Notification(
        Guid id,
        UserId userId,
        NotificationMessages messages)
    {
        Id = id;
        UserId = userId;
        Messages = messages;
        CreatedAt = DateTimeOffset.UtcNow;
    }


    public UserId UserId { get; private set; } = null!;

    public NotificationMessages Messages { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Notification Create(
        Guid id,
        string userId,
        string messages)
    {
        if (id == Guid.Empty) throw new ArgumentException("An id is required.", nameof(id));

        return new Notification(
            id,
            UserId.Create(userId),
            NotificationMessages.Create(messages));
    }
}
