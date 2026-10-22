namespace Shared.Domain.Notifications;

public enum NotificationEventType
{
    Unknown = 0,
    HelpRequestCreated = 1,
    StudentMessageAdded = 2,
    HelpRequestStarted = 3,
    HelpRequestAnswered = 4,
    FaqCreated = 5,
    FaqAnswered = 6
}
