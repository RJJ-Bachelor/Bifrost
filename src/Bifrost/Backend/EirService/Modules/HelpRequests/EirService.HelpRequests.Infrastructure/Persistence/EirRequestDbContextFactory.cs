using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EirService.HelpRequests.Infrastructure.Persistence;

public sealed class EirRequestDbContextFactory : IDesignTimeDbContextFactory<EirRequestDbContext>
{
    public EirRequestDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<EirRequestDbContext>()
            .UseNpgsql("Host=localhost;Database=eir;Username=postgres",
                options => options.MigrationsHistoryTable("__HelpRequestsMigrationsHistory")).Options);
}
