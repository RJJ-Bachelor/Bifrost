namespace Shared.Application.Messaging;

public interface IMessageBus
{
    // Default keeps existing test doubles and integrations source-compatible.
    Task PublishEnvelopeAsync(MessageEnvelope<object> message, string exchangeName, string routingKey,
        CancellationToken cancellationToken = default) =>
        PublishAsync(message.Id, message.Payload, exchangeName, routingKey, cancellationToken, message.Metadata?.Type);

    Task PublishAsync(
        Guid id,
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
