using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MimirService.Infrastructure.Persistence;

namespace MimirService.Test;

public sealed class MessagingMigrationTests
{
    [Fact]
    public void SharedOutboxUpgradeMatchesTheMimirModelAndGeneratesAnAdditiveScript()
    {
        using var db = new MimirRequestDbContext(new DbContextOptionsBuilder<MimirRequestDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=unused").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var script = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("20261116120000_UnifyMessageDelivery", script);
        Assert.Contains("SchemaVersion", script);
        Assert.Contains("NextAttemptAtUtc", script);
        Assert.Contains("DEFAULT 1", script);
    }
}
