using Shared.Application.Messaging;

namespace GnaService.Test.Infrastructure;

internal sealed class CapturingMessageBus<TMessage> : IMessageBus
{
    private readonly TaskCompletionSource<Func<MessageEnvelope<TMessage>, CancellationToken, Task>> handler =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Subscribed => handler.Task;
    public SubscriptionResource Subscription { get; } = new();
    public string? Queue { get; private set; }
    public string? Exchange { get; private set; }
    public string? Route { get; private set; }

    public Task<IAsyncDisposable> SubscribeAsync<T>(string queueName, string exchangeName, string routingKey,
        Func<MessageEnvelope<T>, CancellationToken, Task> callback, CancellationToken cancellationToken = default)
    {
        if (typeof(T) != typeof(TMessage)) throw new InvalidOperationException($"Expected a subscription for {typeof(TMessage).Name}.");
        Queue = queueName;
        Exchange = exchangeName;
        Route = routingKey;
        handler.SetResult((message, token) => callback((MessageEnvelope<T>)(object)message, token));
        return Task.FromResult<IAsyncDisposable>(Subscription);
    }

    public async Task DeliverAsync(MessageEnvelope<TMessage> message, CancellationToken cancellationToken)
    {
        var receive = await handler.Task.WaitAsync(cancellationToken);
        await receive(message, cancellationToken);
    }

    public Task PublishAsync(Guid id, object payload, string exchangeName, string routingKey,
        CancellationToken cancellationToken = default, string? messageType = null) => throw new NotSupportedException();

    internal sealed class SubscriptionResource : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
