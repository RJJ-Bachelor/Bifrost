using GnaService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GnaService.Infrastructure.Persistence;

public sealed class GnaNotificationDbContext(
    DbContextOptions<GnaNotificationDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(notification => notification.Id);

            entity.Property(notification => notification.Id)
                .HasMaxLength(100)
                .IsRequired();
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
        });
    }
}
