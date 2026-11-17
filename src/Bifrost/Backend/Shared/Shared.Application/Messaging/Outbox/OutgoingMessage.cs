namespace Shared.Application.Messaging.Outbox;

public sealed record OutgoingMessage(
    Guid Id, object Payload, string ExchangeName, string RoutingKey,
    string? CoalescingKey = null, int Priority = 0, MessageMetadata? Metadata = null)
{
    public static OutgoingMessage Event(Guid causationId, object payload, string exchangeName, string routingKey,
        DateTimeOffset occurredAt, string? CoalescingKey = null, int Priority = 0)
    {
        var contract = MessageContracts.For(payload.GetType());
        return new(Guid.NewGuid(), payload, exchangeName, routingKey, CoalescingKey, Priority,
            new(contract.Name, contract.Version, occurredAt.ToUniversalTime(), MessageContext.Current?.CorrelationId ?? causationId,
                MessageContext.Current?.MessageId ?? causationId));
    }

    public static OutgoingMessage ModuleEvent(Guid causationId, object payload, string routingKey,
        DateTimeOffset occurredAt) => Event(causationId, payload, InternalMessageRoutes.Exchange, routingKey, occurredAt);

    public static OutgoingMessage Internal(Guid id, object command, string routingKey)
    {
        if (id == Guid.Empty) throw new ArgumentException("A message id is required.", nameof(id));
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);
        return new(id, command, InternalMessageRoutes.Exchange, routingKey);
    }

    // Coalesce only explicitly keyed messages within one commit; keep all other integration events.
    public static IEnumerable<OutgoingMessage> Coalesce(IEnumerable<OutgoingMessage> messages) =>
        messages.Select((message, index) => (Message: message, Index: index))
            .GroupBy(item => (item.Message.CoalescingKey,
                Unique: item.Message.CoalescingKey is null ? item.Index : -1))
            .Select(group => group.OrderByDescending(item => item.Message.Priority).First().Message);
}
