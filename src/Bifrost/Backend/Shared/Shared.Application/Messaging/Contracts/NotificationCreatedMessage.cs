namespace Shared.Application.Messaging.Contracts;

public sealed record NotificationCreatedMessage(string UserId, string Messages);
