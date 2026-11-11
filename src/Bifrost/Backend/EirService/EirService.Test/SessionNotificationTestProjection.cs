using System.Security.Cryptography;
using System.Text;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace EirService.Test;

internal static class SessionNotificationTestProjection
{
    // Existing scenario assertions describe delivery to individual users. Expand the
    // single wire message for those assertions; raw-message tests verify row counts.
    public static IEnumerable<(Guid Id, NotificationCreatedMessage Message)> Expand(Guid messageId, SessionChangedMessage message) =>
        message.UserIds.Select(userId => (
            new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{messageId}:{userId}")).AsSpan(0, 16)),
            new NotificationCreatedMessage(userId, "Sessionen er opdateret.", message.SessionId,
                message.Generation, Change: message.Change, EventType: NotificationEventType.SessionChanged,
                DeliveryType: NotificationDeliveryType.RealtimeOnly)));
}
