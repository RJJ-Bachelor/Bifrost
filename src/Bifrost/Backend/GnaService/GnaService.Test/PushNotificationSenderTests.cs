using GnaService.Application.Repositories;
using GnaService.Domain.Entities;
using GnaService.Infrastructure.Push;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Application.Messaging.Contracts;

namespace GnaService.Test;

public sealed class PushNotificationSenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PushPlaceholderDoesNotClaimDeliveryEvenWhenASubscriptionExists(bool subscribed)
    {
        var store = new SubscriptionStore(subscribed);
        using var cancellation = new CancellationTokenSource();
        var message = new NotificationDeliveryRequestedMessage(Guid.NewGuid(), "student", "Answer", DateTimeOffset.UtcNow);

        var delivered = await new PushNotificationSender(store, NullLogger<PushNotificationSender>.Instance).TrySendAsync(message, cancellation.Token);

        Assert.False(delivered);
        Assert.Equal(message.UserId, store.QueriedUser);
        Assert.Equal(cancellation.Token, store.Token);
    }

    private sealed class SubscriptionStore(bool subscribed) : IPushSubscriptionRepository
    {
        public string? QueriedUser { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<IReadOnlyList<PushSubscription>> GetForUserAsync(string userId, CancellationToken cancellationToken)
        {
            QueriedUser = userId;
            Token = cancellationToken;
            IReadOnlyList<PushSubscription> result = subscribed
                ? [PushSubscription.Create(userId, "https://push.example/subscription", "key", "auth")] : [];
            return Task.FromResult(result);
        }
        public Task SaveAsync(PushSubscription subscription, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoveAsync(string userId, string endpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
