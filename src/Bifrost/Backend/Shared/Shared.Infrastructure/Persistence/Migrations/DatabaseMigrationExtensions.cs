using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Shared.Infrastructure.Persistence.Migrations;

public static class DatabaseMigrationExtensions
{
    public static void MigrateAtStartup(this DatabaseFacade database)
    {
        var databaseCreator = database.GetService<IRelationalDatabaseCreator>();
        if (!databaseCreator.Exists()) databaseCreator.Create();
        // Npgsql reads migration history before EF creates it; create the configured table first.
        database.GetService<IHistoryRepository>().CreateIfNotExists();
        database.Migrate();
    }

    public static async Task MigrateAtStartupAsync(this DatabaseFacade database, CancellationToken cancellationToken = default)
    {
        var databaseCreator = database.GetService<IRelationalDatabaseCreator>();
        if (!await databaseCreator.ExistsAsync(cancellationToken)) await databaseCreator.CreateAsync(cancellationToken);
        await database.GetService<IHistoryRepository>().CreateIfNotExistsAsync(cancellationToken);
        await database.MigrateAsync(cancellationToken);
    }
}
