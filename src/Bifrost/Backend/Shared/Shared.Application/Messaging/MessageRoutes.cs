namespace Shared.Application.Messaging;

public static class MessageRoutes
{
    public const string Sessions = "sessions";
    public const string SessionUpdated = "session.updated";
    public const string Requests = "requests";
    public const string HelpRequestCreated = "request.created";
    public const string HelpRequestUpdated = "request.updated";
    public const string HelpRequestAnswered = "request.answered";
    public const string GeneralizedCreated = "generalized.created";
    public const string Notifications = "notifications";
    public const string NotificationCreated = "gna-notification-requested";
    public const string NotificationDeliveryRequested = "notification.delivery-requested";
}
