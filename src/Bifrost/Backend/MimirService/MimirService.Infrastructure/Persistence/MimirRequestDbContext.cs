using Microsoft.EntityFrameworkCore;
using MimirService.Domain.Entities;

namespace MimirService.Infrastructure.Persistence;

public sealed class MimirRequestDbContext(
    DbContextOptions<MimirRequestDbContext> options) : DbContext(options)
{
    public DbSet<MimirRequest> Requests => Set<MimirRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MimirRequest>(entity =>
        {
            entity.ToTable("requests");
            entity.HasKey(request => request.Id);
            entity.Property(request => request.Id)
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(request => request.Message)
                .HasMaxLength(4000)
                .IsRequired();
            entity.Property(request => request.ReceivedAt)
                .IsRequired();
        });
    }
}
