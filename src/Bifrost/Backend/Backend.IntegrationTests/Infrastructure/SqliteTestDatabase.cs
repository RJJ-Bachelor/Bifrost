using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>A real relational database, held open for one fixture and never shared between fixtures.</summary>
internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public SqliteTestDatabase() => connection.Open();

    public DbContextOptions<T> Options<T>() where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetModelCustomizer>().Options;

    public void Register<T>(IServiceCollection services) where T : DbContext
    {
        services.RemoveAll<DbContextOptions<T>>();
        services.RemoveAll<IDbContextOptionsConfiguration<T>>();
        services.AddDbContext<T>(options => options.UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetModelCustomizer>());
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();
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
