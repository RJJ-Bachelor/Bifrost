namespace Shared.Application.Messaging.Outbox;

public static class InternalMessageRoutes
{
    // A persisted delivery marker, never a RabbitMQ exchange.
    public const string Exchange = "bifrost.internal";
}
