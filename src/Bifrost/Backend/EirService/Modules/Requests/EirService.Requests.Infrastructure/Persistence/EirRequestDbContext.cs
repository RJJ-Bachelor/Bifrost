using EirService.Requests.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EirService.Requests.Infrastructure.Persistence
{
    public sealed class EirRequestDbContext(DbContextOptions<EirRequestDbContext> options)
        : DbContext(options)
    {
        public DbSet<EirRequest> Requests => Set<EirRequest>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
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
            });
        }
    }
}
