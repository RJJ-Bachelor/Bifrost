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
            entity.HasKey(participant => new { participant.SessionId, participant.StudentSubjectId });
            entity.Property(participant => participant.SessionId).ValueGeneratedNever();
            entity.Property(participant => participant.StudentSubjectId).HasMaxLength(100).IsRequired();
            entity.HasIndex(participant => participant.StudentSubjectId);
        });
    }
}
