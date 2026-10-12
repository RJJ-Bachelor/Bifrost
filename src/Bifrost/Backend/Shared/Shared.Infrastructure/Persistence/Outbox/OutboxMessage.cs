using System.Diagnostics;
using System.Text.Json;
using Shared.Application.Messaging.Outbox;

namespace Shared.Infrastructure.Persistence.Outbox;

public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public Guid Id { get; private set; }
    public Guid MessageId { get; private set; }
    public string MessageType { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public string ExchangeName { get; private set; } = null!;
    public string RoutingKey { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public DateTimeOffset? LastAttemptAtUtc { get; private set; }
    public string? TraceParent { get; private set; }
    public string? TraceState { get; private set; }

    public static OutboxMessage Create(OutgoingMessage message, DateTimeOffset now)
    {
        if (message.Id == Guid.Empty) throw new ArgumentException("A message id is required.", nameof(message));
        ArgumentNullException.ThrowIfNull(message.Payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.ExchangeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.RoutingKey);

        return new OutboxMessage
        {
            Id = Guid.NewGuid(), MessageId = message.Id,
            MessageType = message.Payload.GetType().FullName!,
            Payload = JsonSerializer.Serialize(message.Payload, message.Payload.GetType(), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ExchangeName = message.ExchangeName, RoutingKey = message.RoutingKey,
            CreatedAtUtc = now.ToUniversalTime(), TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString
        };
    }

    public void MarkPublished(DateTimeOffset now)
    {
        LastAttemptAtUtc = PublishedAtUtc = now.ToUniversalTime();
    }

    public void MarkFailed(DateTimeOffset now)
    {
        LastAttemptAtUtc = now.ToUniversalTime();
        PublishedAtUtc = null;
    }
}
