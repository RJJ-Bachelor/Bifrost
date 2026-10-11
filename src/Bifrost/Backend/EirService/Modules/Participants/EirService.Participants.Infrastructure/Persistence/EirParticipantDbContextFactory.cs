using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EirService.Participants.Infrastructure.Persistence;

public sealed class EirParticipantDbContextFactory : IDesignTimeDbContextFactory<EirParticipantDbContext>
{
    public EirParticipantDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<EirParticipantDbContext>()
            .UseNpgsql("Host=localhost;Database=eir;Username=postgres",
                options => options.MigrationsHistoryTable("__EFMigrationsHistory", "participants")).Options);
}
