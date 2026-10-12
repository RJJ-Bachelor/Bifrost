using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GnaService.Infrastructure.Persistence;

public sealed class GnaNotificationDbContextFactory : IDesignTimeDbContextFactory<GnaNotificationDbContext>
{
    public GnaNotificationDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<GnaNotificationDbContext>()
            .UseNpgsql("Host=localhost;Database=gna;Username=postgres").Options);
}
