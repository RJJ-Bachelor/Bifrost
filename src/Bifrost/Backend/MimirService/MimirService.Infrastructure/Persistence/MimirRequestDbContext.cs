using Microsoft.EntityFrameworkCore;
using MimirService.Domain.Entities;
using Shared.Infrastructure.Persistence.Outbox;

namespace MimirService.Infrastructure.Persistence;

public sealed class MimirRequestDbContext(
    DbContextOptions<MimirRequestDbContext> options) : DbContext(options)
{
    public DbSet<HelpRequest> Requests => Set<HelpRequest>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddOutbox();
        modelBuilder.Entity<HelpRequest>(entity =>
        {
            entity.ToTable("requests");
            entity.HasKey(request => request.Id);
            entity.Property(request => request.Id).ValueGeneratedNever();
            entity.Property(request => request.Message)
                .HasMaxLength(4000)
                .IsRequired();
            entity.Property(request => request.ReceivedAt)
                .IsRequired();
        });
    }
}
