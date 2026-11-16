namespace GnaService.Api.Endpoints.Common.Notifications.PushSubscriptions;

public sealed class UnregisterPushSubscriptionRequest
{
    public string Endpoint { get; set; } = string.Empty;
}
