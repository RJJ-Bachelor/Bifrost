using Microsoft.EntityFrameworkCore;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Persistence.Messaging;

public sealed class InboxMessage
{
    private InboxMessage() { }
    public string Consumer { get; private set; } = null!;
    public string MessageType { get; private set; } = null!;
    public Guid MessageId { get; private set; }
    public string Envelope { get; private set; } = null!;
    public int Attempts { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public DateTimeOffset? DeadLetteredAtUtc { get; private set; }
    public DateTimeOffset? ReplayRequestedAtUtc { get; private set; }
    public string? LastError { get; private set; }

    public static InboxMessage Create(string consumer, string type, Guid id, string envelope, DateTimeOffset now) =>
        new() { Consumer = consumer, MessageType = type, MessageId = id, Envelope = envelope, ReceivedAtUtc = now };

    public void Claim() => Attempts++;
    public void Complete(DateTimeOffset now)
    {
        CompletedAtUtc = now;
        ReplayRequestedAtUtc = null;
        NextAttemptAtUtc = null;
        LastError = null;
    }
    public void Fail(DateTimeOffset now, Exception error, int maximumAttempts)
    {
        Attempts++;
        LastError = error.GetType().Name + ": " + error.Message;
        if (LastError.Length > 2000) LastError = LastError[..2000];
        var permanent = error is MessageRejectedException or System.Text.Json.JsonException or InvalidDataException;
        DeadLetteredAtUtc = permanent || Attempts >= maximumAttempts ? now : null;
        if (DeadLetteredAtUtc is not null) ReplayRequestedAtUtc = null;
        NextAttemptAtUtc = DeadLetteredAtUtc is null ? now.AddSeconds(Math.Min(60, Math.Pow(2, Math.Min(Attempts - 1, 6)))) : null;
    }
    public void Requeue(DateTimeOffset? requestedAt = null)
    {
        if (CompletedAtUtc is not null) throw new InvalidOperationException("A completed delivery cannot be requeued.");
        DeadLetteredAtUtc = NextAttemptAtUtc = null;
        Attempts = 0;
        ReplayRequestedAtUtc = requestedAt ?? DateTimeOffset.UtcNow;
        LastError = null;
    }
}

public static class InboxModelBuilderExtensions
{
    public static void AddInbox(this ModelBuilder modelBuilder, string schema)
    {
        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("inbox", schema);
            entity.HasKey(message => new { message.Consumer, message.MessageType, message.MessageId });
            entity.Property(message => message.Consumer).HasMaxLength(200);
            entity.Property(message => message.MessageType).HasMaxLength(1000);
            entity.Property(message => message.MessageId).ValueGeneratedNever();
            entity.Property(message => message.Envelope).IsRequired();
            entity.Property(message => message.Attempts).IsConcurrencyToken();
            entity.Property(message => message.LastError).HasMaxLength(2000);
            entity.HasIndex(message => message.DeadLetteredAtUtc);
        });
    }
}
