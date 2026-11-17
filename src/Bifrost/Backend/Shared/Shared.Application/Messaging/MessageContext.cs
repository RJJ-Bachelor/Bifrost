namespace Shared.Application.Messaging;

public sealed record MessageContext(Guid MessageId, Guid CorrelationId)
{
    private static readonly AsyncLocal<MessageContext?> State = new();
    public static MessageContext? Current => State.Value;
    public static IDisposable Begin(Guid id, MessageMetadata? metadata)
    {
        var previous = State.Value;
        State.Value = new(id, metadata?.CorrelationId ?? id);
        return new Restore(previous);
    }
    private sealed class Restore(MessageContext? previous) : IDisposable
    {
        public void Dispose() => State.Value = previous;
    }
}
