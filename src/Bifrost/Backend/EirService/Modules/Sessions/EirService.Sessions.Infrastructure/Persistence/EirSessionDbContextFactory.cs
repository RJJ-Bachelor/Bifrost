using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EirService.Sessions.Infrastructure.Persistence;

public sealed class EirSessionDbContextFactory : IDesignTimeDbContextFactory<EirSessionDbContext>
{
    public EirSessionDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<EirSessionDbContext>()
            .UseNpgsql("Host=localhost;Database=eir;Username=postgres",
                options => options.MigrationsHistoryTable("__EFMigrationsHistory", "sessions")).Options);
}
