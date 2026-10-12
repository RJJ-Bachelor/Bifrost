using EirService.HelpRequests.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.HelpRequests.Infrastructure.Persistence
{
    public sealed class EirRequestDbContext(DbContextOptions<EirRequestDbContext> options)
        : DbContext(options)
    {
        public DbSet<HelpRequest> Requests => Set<HelpRequest>();
        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("helprequests");
            modelBuilder.AddOutbox("helprequests");
            modelBuilder.Entity<HelpRequest>(entity =>
            {
                entity.ToTable("HelpRequest");
                entity.HasKey(request => request.Id);
                entity.Property(request => request.Id).ValueGeneratedNever();
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
