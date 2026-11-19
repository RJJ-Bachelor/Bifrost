using GnaService.Application.Notifications;
using GnaService.Infrastructure.Messaging;
using GnaService.Test.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace GnaService.Test;

public sealed class SessionChangedSubscriberTests
{
    [Fact]
    public async Task RealtimeRefreshBypassesPersistenceAndFailedDeliveryRemainsRetryableWithTheSameId()
    {
        var bus = new CapturingMessageBus<SessionChangedMessage>();
        var observed = new ObservedDeliveries();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMessageBus>(bus);
        services.AddSingleton(observed);
        services.AddScoped<IRealtimeSessionChangeSender, ScopedSender>();
        // No notification repository or database is registered for public realtime refreshes.
        await using var provider = services.BuildServiceProvider();
        using var worker = ActivatorUtilities.CreateInstance<SessionChangedSubscriber>(provider);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(deadline.Token);
        var incoming = new MessageEnvelope<SessionChangedMessage>(Guid.NewGuid(),
            new(["student", "teacher"], Guid.NewGuid(), 2, "Answered"));
        try
        {
            await bus.Subscribed.WaitAsync(deadline.Token);
            await Assert.ThrowsAsync<IOException>(() => bus.DeliverAsync(incoming, deadline.Token));
            await bus.DeliverAsync(incoming, deadline.Token);
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await worker.StopAsync(shutdown.Token);
        }

        Assert.Equal(MessageRoutes.SessionChanged, bus.Queue);
        Assert.Equal(MessageRoutes.Notifications, bus.Exchange);
        Assert.Equal(MessageRoutes.SessionChanged, bus.Route);
        Assert.Equal(new[] { incoming.Id, incoming.Id }, observed.Deliveries.Select(delivery => delivery.Id));
        Assert.All(observed.Deliveries, delivery =>
        {
            Assert.Same(incoming.Payload, delivery.Message);
            Assert.Equal(deadline.Token, delivery.Token);
        });
        Assert.Equal(2, observed.Disposals);
        Assert.True(bus.Subscription.Disposed);
    }

    private sealed class ObservedDeliveries
    {
        public List<(Guid Id, SessionChangedMessage Message, CancellationToken Token)> Deliveries { get; } = [];
        public int Disposals { get; set; }
    }

    private sealed class ScopedSender(ObservedDeliveries observed) : IRealtimeSessionChangeSender, IAsyncDisposable
    {
        public Task SendAsync(Guid notificationId, SessionChangedMessage message, CancellationToken cancellationToken)
        {
            observed.Deliveries.Add((notificationId, message, cancellationToken));
            return observed.Deliveries.Count == 1
                ? Task.FromException(new IOException("SignalR transport unavailable")) : Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            observed.Disposals++;
            return ValueTask.CompletedTask;
        }
    }

}
