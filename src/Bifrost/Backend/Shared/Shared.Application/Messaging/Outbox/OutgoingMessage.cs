namespace Shared.Application.Messaging.Outbox;

public sealed record OutgoingMessage(string Id, object Payload, string ExchangeName, string RoutingKey);
