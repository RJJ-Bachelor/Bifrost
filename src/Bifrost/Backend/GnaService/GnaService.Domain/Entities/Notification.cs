using Shared.Domain.Primitives;
using GnaService.Domain.ValueObjects;
using GnaService.Domain.DomainEvents;
using Shared.Domain.Notifications;

namespace GnaService.Domain.Entities;

public sealed class Notification : AggregateRoot<Guid>
{
    private Notification()
    {
    }

    private Notification(
        Guid id,
        UserId userId,
        NotificationMessages messages,
        DateTimeOffset now,
        Guid? sessionId,
        int? generation,
        Guid? helpRequestId,
        string? change,
        string? status,
        NotificationEventType eventType)
    {
        Id = id;
        UserId = userId;
        Messages = messages;
        CreatedAt = now.ToUniversalTime();
        SessionId = sessionId;
        Generation = generation;
        HelpRequestId = helpRequestId;
        Change = change;
        Status = status;
        EventType = eventType;
    }


    public UserId UserId { get; private set; } = null!;

    public NotificationMessages Messages { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? SessionId { get; private set; }

    public int? Generation { get; private set; }

    public Guid? HelpRequestId { get; private set; }

    public string? Change { get; private set; }

    public string? Status { get; private set; }

    public NotificationEventType EventType { get; private set; }

    public static Notification Create(
        Guid id,
        string userId,
        string messages,
        DateTimeOffset? now = null,
        Guid? sessionId = null,
        int? generation = null,
        Guid? helpRequestId = null,
        string? change = null,
        string? status = null,
        NotificationEventType eventType = NotificationEventType.Unknown)
    {
        if (id == Guid.Empty) throw new ArgumentException("An id is required.", nameof(id));

        var notification = new Notification(
            id,
            UserId.Create(userId),
            NotificationMessages.Create(messages), now ?? DateTimeOffset.UtcNow,
            sessionId, generation, helpRequestId, change, status, eventType);
        notification.RaiseDomainEvent(new NotificationCreatedDomainEvent(notification));
        return notification;
    }
}
