using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using GnaService.Application.Notifications;
using GnaService.Application.Repositories;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Application.Messaging.Contracts;

namespace GnaService.Infrastructure.Push;

public sealed class PushNotificationSender(IPushSubscriptionRepository subscriptions, PushServiceClient client,
    IOptions<WebPushOptions> options, IVapidTokenCache tokenCache, ILogger<PushNotificationSender> logger) : IPushNotificationSender
{
    private static readonly object AuthenticationLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<bool> TrySendAsync(NotificationDeliveryRequestedMessage notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Value.Enabled) return false;

        var saved = await subscriptions.GetForUserAsync(notification.UserId, cancellationToken);
        if (saved.Count == 0) return false;

        using var authentication = new VapidAuthentication(options.Value.PublicKey, options.Value.PrivateKey)
        {
            Subject = new Uri(options.Value.Subject).AbsoluteUri,
            TokenCache = tokenCache
        };

        // This envelope also works with Angular's service worker. Keep the preview within Web Push payload limits.
        var preview = string.Concat(notification.Message.EnumerateRunes().Take(200).Select(rune => rune.ToString()));
        var payload = JsonSerializer.Serialize(new
        {
            notification = new
            {
                title = "Bifrost",
                body = preview.Length < notification.Message.Length ? preview + "…" : preview,
                tag = notification.NotificationId.ToString("N"),
                data = new
                {
                    notification.NotificationId, notification.SessionId, notification.HelpRequestId,
                    notification.Generation, notification.Change, notification.Status, notification.EventType
                }
            }
        }, JsonOptions);
        var message = new PushMessage(payload) { Topic = notification.NotificationId.ToString("N"), TimeToLive = 300 };

        var delivered = false;
        List<Exception>? failures = null;
        foreach (var subscription in saved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Apple requires JWT reuse. Avoid multiple tokens for the same origin on concurrent cache misses.
                lock (AuthenticationLock)
                    authentication.GetVapidSchemeAuthenticationHeaderValueParameter("https://" + new Uri(subscription.Endpoint).Host);
                await client.RequestPushMessageDeliveryAsync(new Lib.Net.Http.WebPush.PushSubscription
                {
                    Endpoint = subscription.Endpoint,
                    Keys = new Dictionary<string, string> { ["p256dh"] = subscription.P256dh, ["auth"] = subscription.Auth }
                }, message, authentication, cancellationToken);
                delivered = true;
            }
            catch (PushServiceClientException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                await subscriptions.RemoveAsync(notification.UserId, subscription.Endpoint, cancellationToken);
                logger.LogDebug("Removed expired push subscription {SubscriptionId} for {UserId}.", subscription.Id, notification.UserId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
                logger.LogWarning("Push subscription {SubscriptionId} failed for notification {NotificationId}; delivery will be retried.",
                    subscription.Id, notification.NotificationId);
            }
        }

        // Retry failed devices even after partial success; tag/topic identify repeated deliveries of the same notification.
        if (failures is { Count: > 0 }) throw new AggregateException("Push delivery failed for one or more subscriptions.", failures);
        return delivered;
    }
}
