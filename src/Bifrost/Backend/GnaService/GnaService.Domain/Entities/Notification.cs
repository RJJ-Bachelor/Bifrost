using Shared.Domain.Primitives;
using GnaService.Domain.ValueObjects;
using GnaService.Domain.DomainEvents;

namespace GnaService.Domain.Entities;

public sealed class Notification : AggregateRoot<Guid>
{
    private Notification()
    {
    }

    private Notification(
        Guid id,
        UserId userId,
        NotificationMessages messages, DateTimeOffset now)
    {
        Id = id;
        UserId = userId;
        Messages = messages;
        CreatedAt = now.ToUniversalTime();
    }


    public UserId UserId { get; private set; } = null!;

    public NotificationMessages Messages { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Notification Create(
        Guid id,
        string userId,
        string messages, DateTimeOffset? now = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("An id is required.", nameof(id));

        var notification = new Notification(
            id,
            UserId.Create(userId),
            NotificationMessages.Create(messages), now ?? DateTimeOffset.UtcNow);
        notification.RaiseDomainEvent(new NotificationCreatedDomainEvent(notification));
        return notification;
    }
}
