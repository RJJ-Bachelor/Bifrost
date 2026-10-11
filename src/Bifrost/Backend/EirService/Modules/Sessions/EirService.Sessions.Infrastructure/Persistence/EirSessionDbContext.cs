using EirService.Sessions.Domain.Entities;
using EirService.Sessions.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace EirService.Sessions.Infrastructure.Persistence;

public sealed class EirSessionDbContext(DbContextOptions<EirSessionDbContext> options) : DbContext(options)
{
    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("sessions");
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
            entity.Property(session => session.CreatedBySubjectId).IsRequired();
            entity.HasIndex(session => session.CreatedBySubjectId);
            entity.Property(session => session.CurrentGeneration).IsConcurrencyToken();
        });

    }
}
