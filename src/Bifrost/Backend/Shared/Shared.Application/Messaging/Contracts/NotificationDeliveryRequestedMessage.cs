using System.Text.Json.Serialization;
using Shared.Domain.Notifications;

namespace Shared.Application.Messaging.Contracts;

public sealed record NotificationDeliveryRequestedMessage(
    Guid NotificationId,
    string UserId,
    string Message,
    DateTimeOffset CreatedAt,
    Guid? SessionId = null,
    Guid? HelpRequestId = null,
    int? Generation = null,
    string? Change = null,
    string? Status = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter<NotificationEventType>))]
    NotificationEventType EventType = NotificationEventType.Unknown,
    [property: JsonConverter(typeof(JsonStringEnumConverter<NotificationDeliveryType>))]
    NotificationDeliveryType DeliveryType = NotificationDeliveryType.RealtimeOrPush);
