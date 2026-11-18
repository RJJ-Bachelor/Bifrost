using GnaService.Application.Services.Messaging;
using GnaService.Infrastructure.Messaging;
using GnaService.Test.Infrastructure;
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
        var bus = new CapturingMessageBus<NotificationCreatedMessage>();
        var observed = new ObservedHandlers();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMessageBus>(bus);
        services.AddSingleton(observed);
        services.AddScoped<INotificationMessageHandler, ScopedHandler>();
        await using var provider = services.BuildServiceProvider();
        using var worker = ActivatorUtilities.CreateInstance<NotificationMessageSubscriber>(provider);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(deadline.Token);
        var first = new MessageEnvelope<NotificationCreatedMessage>(Guid.NewGuid(), new("student", "First"));
        var second = new MessageEnvelope<NotificationCreatedMessage>(Guid.NewGuid(), new("student", "Second"));
        try
        {
            await bus.Subscribed.WaitAsync(deadline.Token);
            await bus.DeliverAsync(first, deadline.Token);
            await bus.DeliverAsync(second, deadline.Token);
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await worker.StopAsync(shutdown.Token);
        }

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
        var bus = new CapturingMessageBus<NotificationCreatedMessage>();
        var failure = new IOException("Persistence failed");
        var observed = new ObservedHandlers { Failure = failure };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMessageBus>(bus);
        services.AddSingleton(observed);
        services.AddScoped<INotificationMessageHandler, ScopedHandler>();
        await using var provider = services.BuildServiceProvider();
        using var worker = ActivatorUtilities.CreateInstance<NotificationMessageSubscriber>(provider);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(deadline.Token);
        try
        {
            await bus.Subscribed.WaitAsync(deadline.Token);
            var thrown = await Assert.ThrowsAsync<IOException>(() => bus.DeliverAsync(new(Guid.NewGuid(), new("student", "Answer")), deadline.Token));
            Assert.Same(failure, thrown);
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await worker.StopAsync(shutdown.Token);
        }
        Assert.Equal(1, observed.Disposals);
        Assert.True(bus.Subscription.Disposed);
    }

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

}
