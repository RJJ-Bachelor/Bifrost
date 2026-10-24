using EirService.Sessions.Domain.Entities;
using EirService.Sessions.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.Sessions.Infrastructure.Persistence;

public sealed class EirSessionDbContext(DbContextOptions<EirSessionDbContext> options) : DbContext(options)
{
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionArchive> Archives => Set<SessionArchive>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("sessions");
        modelBuilder.AddOutbox("sessions");
        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("sessions", table => table.HasCheckConstraint("CK_sessions_DistinctCodes", "\"Code\" <> \"TeacherCode\""));
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Id).ValueGeneratedNever();
            entity.Property(session => session.Title).HasMaxLength(200).IsRequired();
            entity.Property(session => session.Code)
                .HasConversion(code => code.Value, value => new SessionCode(value))
                .HasMaxLength(SessionCode.Length).IsRequired();
            entity.HasIndex(session => session.Code).IsUnique();
            entity.Property(session => session.TeacherCode)
                .HasConversion(code => code.Value, value => new SessionCode(value))
                .HasMaxLength(SessionCode.Length).IsRequired();
            entity.HasIndex(session => session.TeacherCode).IsUnique();
            entity.Property(session => session.CreatedByTeacherId).IsRequired();
            entity.HasIndex(session => session.CreatedByTeacherId);
            entity.Property(session => session.CurrentGeneration).IsConcurrencyToken();
            entity.HasMany(session => session.Archives).WithOne().HasForeignKey(archive => archive.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(session => session.Archives).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<SessionArchive>(entity =>
        {
            entity.ToTable("archives");
            entity.HasKey(archive => archive.Id);
            entity.Property(archive => archive.Id).ValueGeneratedNever();
            entity.Property(archive => archive.Title).HasMaxLength(200).IsRequired();
            entity.HasIndex(archive => new { archive.SessionId, archive.Generation }).IsUnique();
        });

    }
}
