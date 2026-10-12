namespace Shared.Application.Messaging.Outbox;

public sealed record OutgoingMessage(Guid Id, object Payload, string ExchangeName, string RoutingKey);
