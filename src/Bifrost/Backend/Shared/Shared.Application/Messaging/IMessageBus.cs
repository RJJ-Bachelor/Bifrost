namespace Shared.Application.Messaging;

public interface IMessageBus
{
    Task PublishAsync(
        string id,
        object payload,
        string exchangeName,
        string routingKey,
        CancellationToken cancellationToken = default,
        string? messageType = null);

    Task<IAsyncDisposable> SubscribeAsync<T>(
        string queueName,
        string exchangeName,
        string routingKey,
        Func<MessageEnvelope<T>, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default);
}
