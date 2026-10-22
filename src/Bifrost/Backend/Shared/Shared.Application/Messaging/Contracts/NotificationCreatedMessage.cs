using System.Text.Json.Serialization;
using Shared.Domain.Notifications;

namespace Shared.Application.Messaging.Contracts;

public sealed record NotificationCreatedMessage(
    string UserId,
    string Messages,
    Guid? SessionId = null,
    int? Generation = null,
    Guid? HelpRequestId = null,
    string? Change = null,
    string? Status = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter<NotificationEventType>))]
    NotificationEventType EventType = NotificationEventType.Unknown);
