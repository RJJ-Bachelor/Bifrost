using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MimirService.Infrastructure.Persistence;

public sealed class MimirRequestDbContextFactory : IDesignTimeDbContextFactory<MimirRequestDbContext>
{
    public MimirRequestDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<MimirRequestDbContext>()
            .UseNpgsql("Host=localhost;Database=mimir;Username=postgres").Options);
}
