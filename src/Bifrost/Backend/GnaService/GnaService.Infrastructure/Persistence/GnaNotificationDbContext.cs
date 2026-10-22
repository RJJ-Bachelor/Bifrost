using GnaService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence.Outbox;

namespace GnaService.Infrastructure.Persistence;

public sealed class GnaNotificationDbContext(
    DbContextOptions<GnaNotificationDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddOutbox();
        modelBuilder.Entity<PushSubscription>(entity =>
        {
            entity.ToTable("push_subscriptions");
            entity.HasKey(subscription => subscription.Id);
            entity.Property(subscription => subscription.Id).ValueGeneratedNever();
            entity.Property(subscription => subscription.UserId).HasMaxLength(100)
                .HasConversion(user => user.Value, value => GnaService.Domain.ValueObjects.UserId.Create(value)).IsRequired();
            entity.Property(subscription => subscription.Endpoint).HasMaxLength(2048).IsRequired();
            entity.Property(subscription => subscription.P256dh).HasMaxLength(256).IsRequired();
            entity.Property(subscription => subscription.Auth).HasMaxLength(128).IsRequired();
            entity.HasIndex(subscription => new { subscription.UserId, subscription.Endpoint }).IsUnique();
        });
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(notification => notification.Id);

            entity.Property(notification => notification.Id).ValueGeneratedNever();
            entity.Property(notification => notification.UserId)
                .HasMaxLength(100)
                .HasConversion(
                    userId => userId.Value,
                    value => GnaService.Domain.ValueObjects.UserId.Create(value))
                .IsRequired();
            entity.Property(notification => notification.Messages)
                .HasMaxLength(4000)
                .HasConversion(
                    messages => messages.Value,
                    value => GnaService.Domain.ValueObjects.NotificationMessages.Create(value))
                .IsRequired();
            entity.Property(notification => notification.CreatedAt)
                .IsRequired();
            entity.Property(notification => notification.EventType).HasConversion<string>().HasMaxLength(40);
        });
    }
}
