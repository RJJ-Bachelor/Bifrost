using GnaService.Application.Services.Messaging;
using GnaService.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace GnaService.Test;

public sealed class NotificationSubscriberTests
{
    [Fact]
    public async Task EveryDeliveryUsesAndDisposesItsOwnHandlerScopeAndShutdownDisposesTheSubscription()
    {
        var bus = new CapturingBus();
        var observed = new ObservedHandlers();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMessageBus>(bus);
        services.AddSingleton(observed);
        services.AddScoped<INotificationMessageHandler, ScopedHandler>();
        await using var provider = services.BuildServiceProvider();
        using var worker = Subscriber(provider);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(deadline.Token);
        await bus.Subscribed.Task.WaitAsync(deadline.Token);
        var first = new MessageEnvelope<NotificationCreatedMessage>(Guid.NewGuid(), new("student", "First"));
        var second = new MessageEnvelope<NotificationCreatedMessage>(Guid.NewGuid(), new("student", "Second"));

        await bus.DeliverAsync(first, deadline.Token);
        await bus.DeliverAsync(second, deadline.Token);
        await worker.StopAsync(deadline.Token);

        Assert.Equal(MessageRoutes.NotificationCreated, bus.Queue);
        Assert.Equal(MessageRoutes.Notifications, bus.Exchange);
        Assert.Equal(MessageRoutes.NotificationCreated, bus.Route);
        Assert.Equal(new[] { first.Id, second.Id }, observed.Deliveries.Select(delivery => delivery.MessageId));
        Assert.Equal(2, observed.Deliveries.Select(delivery => delivery.ScopeId).Distinct().Count());
        Assert.All(observed.Deliveries, delivery => Assert.Equal(deadline.Token, delivery.Token));
        Assert.Equal(2, observed.Disposals);
        Assert.True(bus.Subscription.Disposed);
    }

    [Fact]
    public async Task HandlerFailureReachesTheTransportAndStillDisposesItsScope()
    {
        var bus = new CapturingBus();
        var failure = new IOException("Persistence failed");
        var observed = new ObservedHandlers { Failure = failure };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMessageBus>(bus);
        services.AddSingleton(observed);
        services.AddScoped<INotificationMessageHandler, ScopedHandler>();
        await using var provider = services.BuildServiceProvider();
        using var worker = Subscriber(provider);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(deadline.Token);
        await bus.Subscribed.Task.WaitAsync(deadline.Token);

        var thrown = await Assert.ThrowsAsync<IOException>(() => bus.DeliverAsync(new(Guid.NewGuid(), new("student", "Answer")), deadline.Token));
        await worker.StopAsync(deadline.Token);

        Assert.Same(failure, thrown);
        Assert.Equal(1, observed.Disposals);
        Assert.True(bus.Subscription.Disposed);
    }

    private static BackgroundService Subscriber(IServiceProvider provider) =>
        (BackgroundService)ActivatorUtilities.CreateInstance(provider,
            typeof(GnaNotificationDbContext).Assembly.GetType("GnaService.Infrastructure.Messaging.NotificationMessageSubscriber", throwOnError: true)!);

    private sealed record Delivery(Guid ScopeId, Guid MessageId, CancellationToken Token);

    private sealed class ObservedHandlers
    {
        public List<Delivery> Deliveries { get; } = [];
        public int Disposals { get; set; }
        public Exception? Failure { get; init; }
    }

    private sealed class ScopedHandler(ObservedHandlers observed) : INotificationMessageHandler, IAsyncDisposable
    {
        private readonly Guid scopeId = Guid.NewGuid();
        public Task HandleAsync(MessageEnvelope<NotificationCreatedMessage> message, CancellationToken cancellationToken)
        {
            observed.Deliveries.Add(new(scopeId, message.Id, cancellationToken));
            return observed.Failure is null ? Task.CompletedTask : Task.FromException(observed.Failure);
        }
        public ValueTask DisposeAsync()
        {
            observed.Disposals++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingBus : IMessageBus
    {
        public TaskCompletionSource Subscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SubscriptionResource Subscription { get; } = new();
        public string? Queue { get; private set; }
        public string? Exchange { get; private set; }
        public string? Route { get; private set; }
        private Func<MessageEnvelope<NotificationCreatedMessage>, CancellationToken, Task> handler = null!;
        public Task<IAsyncDisposable> SubscribeAsync<T>(string queueName, string exchangeName, string routingKey,
            Func<MessageEnvelope<T>, CancellationToken, Task> callback, CancellationToken cancellationToken = default)
        {
            Assert.Equal(typeof(NotificationCreatedMessage), typeof(T));
            Queue = queueName;
            Exchange = exchangeName;
            Route = routingKey;
            handler = (message, token) => callback((MessageEnvelope<T>)(object)message, token);
            Subscribed.TrySetResult();
            return Task.FromResult<IAsyncDisposable>(Subscription);
        }
        public Task DeliverAsync(MessageEnvelope<NotificationCreatedMessage> message, CancellationToken token) => handler(message, token);
        public Task PublishAsync(Guid id, object payload, string exchangeName, string routingKey, CancellationToken cancellationToken = default,
            string? messageType = null) => throw new NotSupportedException();
    }

    private sealed class SubscriptionResource : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
