namespace Shared.Application.Messaging;

// The application handler is shared by local delivery and broker subscribers.
public interface IMessageHandler<T>
{
    Task HandleAsync(MessageEnvelope<T> message, CancellationToken cancellationToken);
}

public interface IMessageReceiver<T>
{
    Task ReceiveAsync(MessageEnvelope<T> message, CancellationToken cancellationToken);
}

public sealed class MessageRejectedException(string message) : Exception(message);
