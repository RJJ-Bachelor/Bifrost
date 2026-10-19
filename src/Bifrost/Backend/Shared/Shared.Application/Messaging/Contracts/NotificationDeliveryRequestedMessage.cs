namespace Shared.Application.Messaging.Contracts;

public sealed record NotificationDeliveryRequestedMessage(
    Guid NotificationId,
    string UserId,
    string Message,
    DateTimeOffset CreatedAt,
    Guid? SessionId = null,
    Guid? HelpRequestId = null);
