using Npgsql;

namespace Shared.Testing;

/// <summary>Owns one isolated database. Dispose application services before disposing this fixture.</summary>
public sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string databaseName;
    private string? adminConnectionString;
    private bool creationAttempted;

    public PostgresTestDatabase(string prefix)
    {
        if (prefix.Length is 0 or > 25 || prefix.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new ArgumentException("Use a database prefix of 1–25 ASCII letters, digits or underscores.", nameof(prefix));
        databaseName = $"{prefix}_{Guid.NewGuid():N}";
    }

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync(string administratorConnectionString, CancellationToken cancellationToken = default)
    {
        if (creationAttempted) throw new InvalidOperationException("The test database has already been initialized.");
        adminConnectionString = new NpgsqlConnectionStringBuilder(administratorConnectionString) { Pooling = false }.ConnectionString;
        ConnectionString = new NpgsqlConnectionStringBuilder(administratorConnectionString) { Database = databaseName }.ConnectionString;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(deadline.Token);
        // Track the attempt too: the server may create the database just before cancellation arrives.
        creationAttempted = true;
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await create.ExecuteNonQueryAsync(deadline.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (!creationAttempted) return;
        // Clear only this fixture's implicit pool. DI-owned data sources are disposed by their host.
        using (var pool = new NpgsqlConnection(ConnectionString)) NpgsqlConnection.ClearPool(pool);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(deadline.Token);
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync(deadline.Token);
        creationAttempted = false;
    }
}
