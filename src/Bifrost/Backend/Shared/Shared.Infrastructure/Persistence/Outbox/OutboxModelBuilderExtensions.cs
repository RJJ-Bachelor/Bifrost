using Microsoft.EntityFrameworkCore;

namespace Shared.Infrastructure.Persistence.Outbox;

public static class OutboxModelBuilderExtensions
{
    public static void AddOutbox(this ModelBuilder modelBuilder, string? schema = null)
    {
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox", schema);
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Id).ValueGeneratedNever();
            entity.Property(message => message.MessageId).IsRequired();
            entity.Property(message => message.MessageType).HasMaxLength(1000).IsRequired();
            entity.Property(message => message.Payload).IsRequired();
            entity.Property(message => message.ExchangeName).HasMaxLength(255).IsRequired();
            entity.Property(message => message.RoutingKey).HasMaxLength(255).IsRequired();
            entity.Property(message => message.TraceParent).HasMaxLength(512);
            entity.Property(message => message.TraceState).HasMaxLength(512);
            entity.HasIndex(message => new { message.PublishedAtUtc, message.CreatedAtUtc });
        });
    }
}
