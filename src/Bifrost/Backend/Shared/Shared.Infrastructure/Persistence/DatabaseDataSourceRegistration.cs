using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Shared.Infrastructure.Persistence;

public static class DatabaseDataSourceRegistration
{
    /// <summary>Shares one connection pool between modules using the same named PostgreSQL database.</summary>
    public static IServiceCollection AddSharedPostgresDataSource(
        this IServiceCollection services, string databaseName, DbConnectionStringBuilder connectionString,
        Func<string, DbDataSource> createDataSource, int defaultMaximumPoolSize = 40)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentNullException.ThrowIfNull(createDataSource);
        ArgumentOutOfRangeException.ThrowIfLessThan(defaultMaximumPoolSize, 1);

        // Provider builders recognize aliases and distinguish an explicit limit from their default.
        // Keep the caller's pool size, including an explicit value equal to the provider default.
        if (!connectionString.ShouldSerialize("Maximum Pool Size"))
            connectionString["Maximum Pool Size"] = defaultMaximumPoolSize;

        var configuredConnectionString = connectionString.ConnectionString;
        services.TryAddKeyedSingleton<DbDataSource>(databaseName,
            (_, _) => createDataSource(configuredConnectionString));
        return services;
    }
}
