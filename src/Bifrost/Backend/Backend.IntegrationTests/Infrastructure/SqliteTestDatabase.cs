using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>An isolated relational database with a separate connection per context for concurrent dispatch.</summary>
internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"bifrost-test-{Guid.NewGuid():N}.sqlite");
    private readonly string connectionString;

    public SqliteTestDatabase()
    {
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
            DefaultTimeout = 30
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteScalar();
    }

    public DbContextOptions<T> Options<T>() where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseSqlite(connectionString)
            .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetModelCustomizer>().Options;

    public void Register<T>(IServiceCollection services, Action<DbContextOptionsBuilder>? configure = null) where T : DbContext
    {
        services.RemoveAll<DbContextOptions<T>>();
        services.RemoveAll<IDbContextOptionsConfiguration<T>>();
        services.AddDbContext<T>(options =>
        {
            options.UseSqlite(connectionString).ReplaceService<IModelCustomizer, SqliteDateTimeOffsetModelCustomizer>();
            configure?.Invoke(options);
        });
    }

    public ValueTask DisposeAsync()
    {
        File.Delete(databasePath);
        File.Delete(databasePath + "-wal");
        File.Delete(databasePath + "-shm");
        return ValueTask.CompletedTask;
    }
}

// SQLite cannot compare DateTimeOffset in ORDER BY. Store UTC ticks for these tests.
// Production PostgreSQL mappings remain unchanged; this is not a PostgreSQL compatibility test.
internal sealed class SqliteDateTimeOffsetModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
        {
            if (property.ClrType == typeof(DateTimeOffset))
                property.SetValueConverter(new ValueConverter<DateTimeOffset, long>(
                    value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero)));
            else if (property.ClrType == typeof(DateTimeOffset?))
                property.SetValueConverter(new ValueConverter<DateTimeOffset?, long?>(
                    value => value.HasValue ? value.Value.UtcTicks : null,
                    value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null));
        }
    }
}
