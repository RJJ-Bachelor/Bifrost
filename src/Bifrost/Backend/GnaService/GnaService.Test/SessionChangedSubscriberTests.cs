using GnaService.Application.Notifications;
using GnaService.Infrastructure.Persistence;
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
        var bus = new CapturingBus();
        var observed = new ObservedDeliveries();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMessageBus>(bus);
        services.AddSingleton(observed);
        services.AddScoped<IRealtimeSessionChangeSender, ScopedSender>();
        // No notification repository or database is registered for public realtime refreshes.
        await using var provider = services.BuildServiceProvider();
        using var worker = (BackgroundService)ActivatorUtilities.CreateInstance(provider,
            typeof(GnaNotificationDbContext).Assembly.GetType(
                "GnaService.Infrastructure.Messaging.SessionChangedSubscriber", throwOnError: true)!);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(deadline.Token);
        await bus.Subscribed.Task.WaitAsync(deadline.Token);
        var incoming = new MessageEnvelope<SessionChangedMessage>(Guid.NewGuid(),
            new(["student", "teacher"], Guid.NewGuid(), 2, "Answered"));

        await Assert.ThrowsAsync<IOException>(() => bus.DeliverAsync(incoming, deadline.Token));
        await bus.DeliverAsync(incoming, deadline.Token);
        await worker.StopAsync(deadline.Token);

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
        Assert.True(bus.Disposed);
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

    private sealed class CapturingBus : IMessageBus, IAsyncDisposable
    {
        public TaskCompletionSource Subscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Queue { get; private set; }
        public string? Exchange { get; private set; }
        public string? Route { get; private set; }
        public bool Disposed { get; private set; }
        private Func<MessageEnvelope<SessionChangedMessage>, CancellationToken, Task> handler = null!;

        public Task<IAsyncDisposable> SubscribeAsync<T>(string queueName, string exchangeName, string routingKey,
            Func<MessageEnvelope<T>, CancellationToken, Task> callback, CancellationToken cancellationToken = default)
        {
            Assert.Equal(typeof(SessionChangedMessage), typeof(T));
            Queue = queueName;
            Exchange = exchangeName;
            Route = routingKey;
            handler = (message, token) => callback((MessageEnvelope<T>)(object)message, token);
            Subscribed.TrySetResult();
            return Task.FromResult<IAsyncDisposable>(this);
        }

        public Task DeliverAsync(MessageEnvelope<SessionChangedMessage> message, CancellationToken token) => handler(message, token);

        public Task PublishAsync(Guid id, object payload, string exchangeName, string routingKey,
            CancellationToken cancellationToken = default, string? messageType = null) => throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
