using Microsoft.EntityFrameworkCore;
using MimirService.Domain.Entities;
using Shared.Infrastructure.Persistence.Outbox;

namespace MimirService.Infrastructure.Persistence;

public sealed class MimirRequestDbContext(
    DbContextOptions<MimirRequestDbContext> options) : DbContext(options)
{
    public DbSet<HelpRequest> Requests => Set<HelpRequest>();
    public DbSet<Generalization> Generalizations => Set<Generalization>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddOutbox();
        modelBuilder.Entity<HelpRequest>(entity =>
        {
            entity.ToTable("requests");
            entity.HasKey(request => request.Id);
            entity.Property(request => request.Id).ValueGeneratedNever();
            entity.Property(request => request.SessionId)
                .IsRequired();
            entity.Property(request => request.Generation)
                .IsRequired();
            entity.HasIndex(request => new { request.SessionId, request.Generation });
            entity.Property(request => request.Message)
                .HasMaxLength(4000)
                .IsRequired();
            entity.Property(request => request.ReceivedAt)
                .IsRequired();
            entity.HasOne<Generalization>()
                .WithMany()
                .HasForeignKey(request => request.GeneralizationId);
        });
        modelBuilder.Entity<Generalization>(entity =>
        {
            entity.ToTable("generalizations");
            entity.HasKey(generalization => generalization.Id);
            entity.Property(generalization => generalization.Id).ValueGeneratedNever();
            entity.Property(generalization => generalization.SessionId)
                .IsRequired();
            entity.Property(generalization => generalization.Generation)
                .IsRequired();
            entity.HasIndex(generalization => new { generalization.SessionId, generalization.Generation });
            entity.Property(generalization => generalization.Question)
                .HasMaxLength(Generalization.MaxQuestionLength)
                .IsRequired();
            entity.Property(generalization => generalization.CreatedAt)
                .IsRequired();
        });
    }
}
