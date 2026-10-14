using GnaService.Domain.ValueObjects;
using Shared.Domain.Primitives;

namespace GnaService.Domain.Entities;

public sealed class PushSubscription : Entity<Guid>
{
    private PushSubscription() { }
    public UserId UserId { get; private set; } = null!;
    public string Endpoint { get; private set; } = null!;
    public string P256dh { get; private set; } = null!;
    public string Auth { get; private set; } = null!;

    public static PushSubscription Create(string userId, string endpoint, string p256dh, string auth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (endpoint.Length > 2048 || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new ArgumentException("A valid HTTPS push endpoint is required.", nameof(endpoint));
        var subscription = new PushSubscription { Id = Guid.NewGuid(), UserId = UserId.Create(userId), Endpoint = endpoint };
        subscription.UpdateKeys(p256dh, auth);
        return subscription;
    }

    public void UpdateKeys(string p256dh, string auth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(p256dh);
        ArgumentException.ThrowIfNullOrWhiteSpace(auth);
        if (p256dh.Length > 256 || auth.Length > 128) throw new ArgumentException("Invalid push subscription keys.");
        P256dh = p256dh;
        Auth = auth;
    }
}
