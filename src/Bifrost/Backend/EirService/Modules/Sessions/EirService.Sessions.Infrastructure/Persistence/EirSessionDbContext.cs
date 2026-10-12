using EirService.Sessions.Domain.Entities;
using EirService.Sessions.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.Sessions.Infrastructure.Persistence;

public sealed class EirSessionDbContext(DbContextOptions<EirSessionDbContext> options) : DbContext(options)
{
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("sessions");
        modelBuilder.AddOutbox("sessions");
        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Id).ValueGeneratedNever();
            entity.Property(session => session.Title).HasMaxLength(200).IsRequired();
            entity.Property(session => session.Code)
                .HasConversion(code => code.Value, value => new SessionCode(value))
                .HasMaxLength(SessionCode.Length).IsRequired();
            entity.HasIndex(session => session.Code).IsUnique();
            entity.Property(session => session.CreatedByTeacherId).IsRequired();
            entity.HasIndex(session => session.CreatedByTeacherId);
            entity.Property(session => session.CurrentGeneration).IsConcurrencyToken();
        });

    }
}
