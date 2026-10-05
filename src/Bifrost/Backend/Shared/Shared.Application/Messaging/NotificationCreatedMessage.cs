namespace Shared.Application.Messaging;

public sealed record NotificationCreatedMessage(string UserId, string Messages);
