namespace Shared.Application.Messaging.Contracts;

public sealed record NotificationCreatedMessage(string UserId, string Messages, Guid? SessionId = null, int? Generation = null);
