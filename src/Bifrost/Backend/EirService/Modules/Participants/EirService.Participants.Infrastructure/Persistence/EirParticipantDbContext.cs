using EirService.Participants.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EirService.Participants.Infrastructure.Persistence;

public sealed class EirParticipantDbContext(DbContextOptions<EirParticipantDbContext> options) : DbContext(options)
{
    public DbSet<SessionParticipant> Participants => Set<SessionParticipant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("participants");
        modelBuilder.Entity<SessionParticipant>(entity =>
        {
            entity.ToTable("session_participants");
            entity.Ignore(participant => participant.Id);
            entity.HasKey(participant => new { participant.SessionId, participant.SubjectId, participant.Role });
            entity.Property(participant => participant.SessionId).ValueGeneratedNever();
            entity.Property(participant => participant.SubjectId).HasMaxLength(100).IsRequired();
            entity.Property(participant => participant.Role).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(participant => new { participant.SubjectId, participant.Role });
        });
    }
}
