using EirService.KnowledgeBase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.KnowledgeBase.Infrastructure.Persistence;

public sealed class EirKnowledgeBaseDbContext(DbContextOptions<EirKnowledgeBaseDbContext> options) : DbContext(options)
{
    public DbSet<FaqAnswer> Faqs => Set<FaqAnswer>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("knowledgebase");
        modelBuilder.AddOutbox("knowledgebase");
        modelBuilder.Entity<FaqAnswer>(entity =>
        {
            entity.ToTable("FaqAnswer");
            entity.HasKey(faq => faq.Id);
            entity.Property(faq => faq.Id).ValueGeneratedNever();
            entity.Property(faq => faq.Question).HasMaxLength(4000).IsRequired();
            entity.Property(faq => faq.Answer).HasMaxLength(4000);
            entity.Property(faq => faq.AnsweredByTeacherId).HasMaxLength(100);
            entity.Ignore(faq => faq.HelpRequestIds);
            entity.Property<Guid[]>("helpRequestIds").HasColumnName("HelpRequestIds");
            entity.Property<uint>("xmin").IsRowVersion();
            entity.HasIndex(faq => new { faq.SessionId, faq.Generation });
        });
    }
}
