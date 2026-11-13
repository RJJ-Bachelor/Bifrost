namespace GnaService.Api.Endpoints.Common.Notifications.PushSubscriptions;

public sealed class RegisterPushSubscriptionRequest
{
    public string Endpoint { get; set; } = string.Empty;
    public PushSubscriptionKeys? Keys { get; set; }
}

public sealed class PushSubscriptionKeys
{
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
}
