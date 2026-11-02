using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using EirService.Participants.Infrastructure.Persistence;
using EirService.Sessions.Infrastructure.Persistence;
using GnaService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Backend.IntegrationTests.Persistence;

// These tests integrate EF's migration assembly, model and PostgreSQL SQL generator.
// They deliberately do not claim that the resulting SQL was executed by PostgreSQL.
public sealed class MigrationContractTests
{
    [Theory]
    [InlineData("sessions", "sessions", "archives")]
    [InlineData("participants", "session_participants", "DisplayName")]
    [InlineData("helprequests", "HelpRequest", "RequestNumber")]
    [InlineData("knowledgebase", "FaqAnswer", "Question")]
    [InlineData("notifications", "notifications", "push_subscriptions")]
    public void PostgreSqlUpgradeScriptIncludesEveryMigrationAndExpectedTables(
        string module, string table, string expectedColumnOrTable)
    {
        using var context = CreateContext(module);
        var migrations = context.GetService<IMigrationsAssembly>();
        Assert.NotEmpty(migrations.Migrations);

        var script = context.GetService<IMigrator>().GenerateScript(
            options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Contains("CREATE TABLE", script);
        Assert.Contains(table, script);
        Assert.Contains(expectedColumnOrTable, script);
        Assert.Contains("__EFMigrationsHistory", script);
        Assert.Contains("COMMIT;", script);
        foreach (var migrationId in migrations.Migrations.Keys)
            Assert.Contains(migrationId, script);
    }

    [Theory]
    [InlineData("sessions", "sessions")]
    [InlineData("participants", "session_participants")]
    [InlineData("helprequests", "HelpRequest")]
    [InlineData("knowledgebase", "FaqAnswer")]
    [InlineData("notifications", "notifications")]
    public void PostgreSqlRollbackScriptReversesEntireMigrationHistory(string module, string table)
    {
        using var context = CreateContext(module);
        var migrations = context.GetService<IMigrationsAssembly>().Migrations;
        var latest = migrations.Keys.Last();

        var script = context.GetService<IMigrator>().GenerateScript(latest, Migration.InitialDatabase);

        Assert.Contains("DROP TABLE", script);
        Assert.Contains(table, script);
        foreach (var migrationId in migrations.Keys)
            Assert.Contains(migrationId, script);
        Assert.Contains("DELETE FROM", script);
    }

    [Theory]
    [InlineData("sessions")]
    [InlineData("participants")]
    [InlineData("helprequests")]
    [InlineData("knowledgebase")]
    [InlineData("notifications")]
    public void SnapshotAndCurrentModelHaveMatchingTablesAndKeys(string module)
    {
        using var context = CreateContext(module);
        var assembly = context.GetService<IMigrationsAssembly>();
        var snapshot = Assert.IsAssignableFrom<ModelSnapshot>(assembly.ModelSnapshot);
        Assert.False(context.Database.HasPendingModelChanges(),
            $"The {module} model contains changes that are missing from the migration snapshot.");
        var snapshotEntities = snapshot.Model.GetEntityTypes().OrderBy(entity => entity.Name).ToArray();
        var currentEntities = context.Model.GetEntityTypes().OrderBy(entity => entity.Name).ToArray();

        Assert.NotEmpty(snapshotEntities);
        Assert.Equal(snapshotEntities.Select(entity => entity.Name), currentEntities.Select(entity => entity.Name));
        foreach (var (snapshotEntity, currentEntity) in snapshotEntities.Zip(currentEntities))
        {
            Assert.Equal(snapshotEntity.GetTableName(), currentEntity.GetTableName());
            Assert.Equal(snapshotEntity.GetSchema(), currentEntity.GetSchema());
            Assert.Equal(snapshotEntity.FindPrimaryKey()!.Properties.Select(property => property.Name),
                currentEntity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        }
    }

    private static DbContext CreateContext(string module) => module switch
    {
        "sessions" => new EirSessionDbContext(Options<EirSessionDbContext>()),
        "participants" => new EirParticipantDbContext(Options<EirParticipantDbContext>()),
        "helprequests" => new EirRequestDbContext(Options<EirRequestDbContext>()),
        "knowledgebase" => new EirKnowledgeBaseDbContext(Options<EirKnowledgeBaseDbContext>()),
        "notifications" => new GnaNotificationDbContext(Options<GnaNotificationDbContext>()),
        _ => throw new ArgumentOutOfRangeException(nameof(module), module, "Unknown backend module.")
    };

    private static DbContextOptions<TContext> Options<TContext>() where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseNpgsql("Host=localhost;Database=migration_contract_only;Username=unused;Password=unused")
            .Options;
}
