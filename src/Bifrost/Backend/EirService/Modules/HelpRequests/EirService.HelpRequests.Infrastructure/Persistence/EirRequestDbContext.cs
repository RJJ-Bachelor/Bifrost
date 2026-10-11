using EirService.HelpRequests.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.HelpRequests.Infrastructure.Persistence
{
    public sealed class EirRequestDbContext(DbContextOptions<EirRequestDbContext> options)
        : DbContext(options)
    {
        public DbSet<EirRequest> Requests => Set<EirRequest>();
        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddOutbox();
            modelBuilder.Entity<EirRequest>(entity =>
            {
                entity.ToTable("requests");
                entity.HasKey(request => request.Id);
                entity.Property(request => request.Id)
                    .HasMaxLength(100)
                    .IsRequired();
                entity.Property(request => request.Message)
                    .HasMaxLength(4000)
                    .IsRequired();
                entity.Property(request => request.UserId).HasMaxLength(100);
                entity.HasIndex(request => request.UserId);
                entity.HasIndex(request => new { request.SessionId, request.Generation, request.UserId });
            });
        }
    }
}
