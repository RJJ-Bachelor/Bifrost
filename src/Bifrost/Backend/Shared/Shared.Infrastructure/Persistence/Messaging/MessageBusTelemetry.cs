using System.Diagnostics;
using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Shared.Infrastructure.Persistence.Messaging;

internal static class MessageBusTelemetry
{
    internal const string SourceName = "Bifrost.Messaging";
    private static readonly ActivitySource Source = new(SourceName);
    private static readonly DistributedContextPropagator Propagator =
        DistributedContextPropagator.CreateDefaultPropagator();

    internal static Activity? StartPublishActivity(string exchangeName, string routingKey, string messageId)
    {
        return Source.StartActivity($"publish {routingKey}", ActivityKind.Producer, default(ActivityContext),
            tags: new ActivityTagsCollection
            {
                { "messaging.system", "rabbitmq" },
                { "messaging.operation.type", "send" },
                { "messaging.operation.name", "publish" },
                { "messaging.destination.name", exchangeName },
                { "messaging.rabbitmq.destination.routing_key", routingKey },
                { "messaging.message.id", messageId }
            });
    }

    internal static void InjectContext(BasicProperties properties)
    {
        if (Activity.Current is null)
        {
            return;
        }

        properties.Headers ??= new Dictionary<string, object?>();
        Propagator.Inject(Activity.Current, properties.Headers, (carrier, name, value) =>
            ((IDictionary<string, object?>)carrier!)[name] = Encoding.UTF8.GetBytes(value));
    }

    internal static Activity? StartConsumerActivity(string queueName, BasicDeliverEventArgs delivery)
    {
        Propagator.ExtractTraceIdAndState(delivery.BasicProperties.Headers, ReadHeader,
            out var traceParent, out var traceState);
        ActivityContext.TryParse(traceParent, traceState, isRemote: true, out var parentContext);

        // Use the message as the parent so Aspire displays the whole workflow in one trace.
        var activity = Source.StartActivity($"process {delivery.RoutingKey}", ActivityKind.Consumer,
            parentContext, tags: new ActivityTagsCollection
            {
                { "messaging.system", "rabbitmq" },
                { "messaging.operation.type", "process" },
                { "messaging.operation.name", "process" },
                { "messaging.destination.name", delivery.Exchange },
                { "messaging.rabbitmq.destination.routing_key", delivery.RoutingKey },
                { "messaging.rabbitmq.queue.name", queueName },
                { "messaging.rabbitmq.message.redelivered", delivery.Redelivered },
                { "messaging.message.body.size", delivery.Body.Length }
            });

        if (activity is not null &&
            Propagator.ExtractBaggage(delivery.BasicProperties.Headers, ReadHeader) is { } baggage)
        {
            foreach (var item in baggage)
            {
                activity.AddBaggage(item.Key, item.Value);
            }
        }

        return activity;
    }

    internal static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.SetTag("error.type", exception.GetType().FullName);
        activity?.AddException(exception);
    }

    private static void ReadHeader(object? carrier, string name, out string? value,
        out IEnumerable<string>? values)
    {
        values = null;
        value = carrier is IDictionary<string, object?> headers && headers.TryGetValue(name, out var rawValue)
            ? rawValue switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text => text,
                _ => null
            }
            : null;
    }
}
