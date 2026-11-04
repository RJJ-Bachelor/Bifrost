using GnaService.Domain.DomainEvents;
using GnaService.Domain.Entities;
using GnaService.Domain.ValueObjects;
using Shared.Domain.Notifications;

namespace GnaService.Test;

public sealed class NotificationDomainTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void EmptyRecipientAndMessageAreRejected(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => UserId.Create(value!));
        Assert.ThrowsAny<ArgumentException>(() => NotificationMessages.Create(value!));
    }

    [Fact]
    public void ValueObjectLengthLimitsAcceptTheBoundaryAndRejectOneCharacterMore()
    {
        var user = new string('u', 100);
        var message = new string('m', 4000);

        Assert.Equal(user, UserId.Create(user).Value);
        Assert.Equal(UserId.Create(user), UserId.Create(user));
        Assert.Equal(message, NotificationMessages.Create(message).Value);
        Assert.Equal(NotificationMessages.Create(message), NotificationMessages.Create(message));
        Assert.Throws<ArgumentException>(() => UserId.Create(user + "u"));
        Assert.Throws<ArgumentException>(() => NotificationMessages.Create(message + "m"));
    }

    [Fact]
    public void NotificationRequiresAnIdAndValidDomainValues()
    {
        Assert.Throws<ArgumentException>(() => Notification.Create(Guid.Empty, "student", "Answer"));
        Assert.ThrowsAny<ArgumentException>(() => Notification.Create(Guid.NewGuid(), "", "Answer"));
        Assert.ThrowsAny<ArgumentException>(() => Notification.Create(Guid.NewGuid(), "student", ""));
    }

    [Fact]
    public void CreationPreservesTheContextNormalizesTimeAndRaisesOneDomainEvent()
    {
        var id = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 10, 24, 14, 30, 0, TimeSpan.FromHours(2));

        var notification = Notification.Create(id, "student", "Answer", now, sessionId, 3, requestId,
            "Answered", "Completed", NotificationEventType.HelpRequestAnswered, NotificationDeliveryType.RealtimeOnly);

        Assert.Equal(id, notification.Id);
        Assert.Equal("student", notification.UserId.Value);
        Assert.Equal("Answer", notification.Messages.Value);
        Assert.Equal(now.ToUniversalTime(), notification.CreatedAt);
        Assert.Equal(TimeSpan.Zero, notification.CreatedAt.Offset);
        Assert.Equal(sessionId, notification.SessionId);
        Assert.Equal(3, notification.Generation);
        Assert.Equal(requestId, notification.HelpRequestId);
        Assert.Equal("Answered", notification.Change);
        Assert.Equal("Completed", notification.Status);
        Assert.Equal(NotificationEventType.HelpRequestAnswered, notification.EventType);
        Assert.Equal(NotificationDeliveryType.RealtimeOnly, notification.DeliveryType);
        Assert.Same(notification, Assert.IsType<NotificationCreatedDomainEvent>(Assert.Single(notification.GetDomainEvents())).Notification);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative/path")]
    [InlineData("not a URL")]
    [InlineData("http://push.example/subscription")]
    [InlineData("ftp://push.example/subscription")]
    public void PushSubscriptionRequiresAnAbsoluteHttpsEndpoint(string? endpoint)
    {
        Assert.ThrowsAny<ArgumentException>(() => PushSubscription.Create("student", endpoint!, "key", "auth"));
    }

    [Fact]
    public void PushEndpointLengthLimitIsEnforced()
    {
        const string prefix = "https://push.example/";
        var endpoint = prefix + new string('x', 2048 - prefix.Length);

        Assert.Equal(endpoint, PushSubscription.Create("student", endpoint, "key", "auth").Endpoint);
        Assert.Throws<ArgumentException>(() => PushSubscription.Create("student", endpoint + "x", "key", "auth"));
    }

    [Theory]
    [InlineData(null, "auth")]
    [InlineData("", "auth")]
    [InlineData(" ", "auth")]
    [InlineData("key", null)]
    [InlineData("key", "")]
    [InlineData("key", " ")]
    public void InvalidReplacementKeysDoNotPartiallyChangeTheSubscription(string? key, string? auth)
    {
        var subscription = PushSubscription.Create("student", "https://push.example/subscription", "original-key", "original-auth");
        var id = subscription.Id;

        Assert.ThrowsAny<ArgumentException>(() => subscription.UpdateKeys(key!, auth!));

        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(id, subscription.Id);
        Assert.Equal("original-key", subscription.P256dh);
        Assert.Equal("original-auth", subscription.Auth);
    }

    [Fact]
    public void PushKeysAcceptTheirMaximumLengthsAndRejectLongerKeysAtomically()
    {
        var key = new string('k', 256);
        var auth = new string('a', 128);
        var subscription = PushSubscription.Create("student", "https://push.example/subscription", key, auth);

        Assert.Throws<ArgumentException>(() => subscription.UpdateKeys(key + "k", auth));
        Assert.Throws<ArgumentException>(() => subscription.UpdateKeys("replacement", auth + "a"));
        Assert.Equal(key, subscription.P256dh);
        Assert.Equal(auth, subscription.Auth);
    }
}
