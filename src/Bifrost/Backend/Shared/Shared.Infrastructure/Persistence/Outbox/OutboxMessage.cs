using System.Diagnostics;
using System.Text.Json;
using Shared.Application.Messaging;
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
    public int SchemaVersion { get; private set; } = 1;
    public DateTimeOffset? OccurredAtUtc { get; private set; }
    public Guid? CorrelationId { get; private set; }
    public Guid? CausationId { get; private set; }
    public int FailureCount { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public DateTimeOffset? DeadLetteredAtUtc { get; private set; }
    public string? LastError { get; private set; }

    public MessageMetadata Metadata => new(MessageType, SchemaVersion,
        OccurredAtUtc ?? CreatedAtUtc, CorrelationId, CausationId);

    public static OutboxMessage Create(OutgoingMessage message, DateTimeOffset now)
    {
        if (message.Id == Guid.Empty) throw new ArgumentException("A message id is required.", nameof(message));
        ArgumentNullException.ThrowIfNull(message.Payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.ExchangeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.RoutingKey);

        return new OutboxMessage
        {
            Id = Guid.NewGuid(), MessageId = message.Id,
            MessageType = message.Metadata?.Type ?? MessageContracts.For(message.Payload.GetType()).Name,
            SchemaVersion = message.Metadata?.Version ?? MessageContracts.For(message.Payload.GetType()).Version,
            OccurredAtUtc = message.Metadata?.OccurredAtUtc.ToUniversalTime() ?? now.ToUniversalTime(),
            CorrelationId = message.Metadata?.CorrelationId ?? MessageContext.Current?.CorrelationId,
            CausationId = message.Metadata?.CausationId ?? MessageContext.Current?.MessageId,
            Payload = JsonSerializer.Serialize(message.Payload, message.Payload.GetType(), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ExchangeName = message.ExchangeName, RoutingKey = message.RoutingKey,
            CreatedAtUtc = now.ToUniversalTime(), TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString
        };
    }

    public void MarkPublished(DateTimeOffset now)
    {
        LastAttemptAtUtc = PublishedAtUtc = now.ToUniversalTime();
        NextAttemptAtUtc = null;
        LastError = null;
    }

    public void ScheduleRetry(DateTimeOffset now, Exception error, bool permanent = false)
    {
        MarkFailed(now);
        FailureCount++;
        LastError = error.GetType().Name + ": " + error.Message;
        if (LastError.Length > 2000) LastError = LastError[..2000];
        DeadLetteredAtUtc = permanent ? now.ToUniversalTime() : null;
        NextAttemptAtUtc = permanent ? null : now.ToUniversalTime().AddSeconds(Math.Min(60, Math.Pow(2, Math.Min(FailureCount - 1, 6))));
    }

    public void Requeue()
    {
        PublishedAtUtc = DeadLetteredAtUtc = NextAttemptAtUtc = LastAttemptAtUtc = null;
        FailureCount = 0;
        LastError = null;
    }

    public void MarkFailed(DateTimeOffset now)
    {
        LastAttemptAtUtc = now.ToUniversalTime();
        PublishedAtUtc = null;
    }
}
