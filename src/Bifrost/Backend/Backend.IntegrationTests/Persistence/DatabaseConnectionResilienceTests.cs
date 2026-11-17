using GnaService.Infrastructure;
using GnaService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MimirService.Infrastructure;
using MimirService.Infrastructure.Persistence;
using Npgsql;

namespace Backend.IntegrationTests.Persistence;

public sealed class DatabaseConnectionResilienceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TemporaryConnectionTimeoutIsRetried(bool mimir)
    {
        await using var provider = BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var strategy = Context(scope.ServiceProvider, mimir).Database.CreateExecutionStrategy();
        var attempts = 0;

        await strategy.ExecuteAsync(() =>
        {
            if (++attempts < 3) throw ConnectionTimeout();
            return Task.CompletedTask;
        });

        Assert.Equal(3, attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistentConnectionTimeoutStopsAfterThreeRetries(bool mimir)
    {
        await using var provider = BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var strategy = Context(scope.ServiceProvider, mimir).Database.CreateExecutionStrategy();
        var attempts = 0;
        var timeout = ConnectionTimeout();

        var error = await Assert.ThrowsAsync<RetryLimitExceededException>(() => strategy.ExecuteAsync(() =>
        {
            attempts++;
            return Task.FromException(timeout);
        }));

        Assert.Equal(4, attempts);
        Assert.Same(timeout, error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermanentDatabaseErrorIsNotRetried(bool mimir)
    {
        await using var provider = BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var strategy = Context(scope.ServiceProvider, mimir).Database.CreateExecutionStrategy();
        var attempts = 0;
        var duplicate = new PostgresException("Duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);

        var error = await Assert.ThrowsAsync<PostgresException>(() => strategy.ExecuteAsync(() =>
        {
            attempts++;
            return Task.FromException(duplicate);
        }));

        Assert.Equal(1, attempts);
        Assert.Same(duplicate, error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledOperationIsNotRetried(bool mimir)
    {
        await using var provider = BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var strategy = Context(scope.ServiceProvider, mimir).Database.CreateExecutionStrategy();
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => strategy.ExecuteAsync(token =>
        {
            attempts++;
            cancellation.Cancel();
            return Task.FromCanceled(token);
        }, cancellation.Token));

        Assert.Equal(1, attempts);
    }

    private static DbContext Context(IServiceProvider provider, bool mimir) => mimir
        ? provider.GetRequiredService<MimirRequestDbContext>()
        : provider.GetRequiredService<GnaNotificationDbContext>();

    private static NpgsqlException ConnectionTimeout() => new("Connection timed out", new TimeoutException());

    private static ServiceProvider BuildServices()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:gna"] = "Host=unused;Database=gna;Username=test",
            ["ConnectionStrings:mimir"] = "Host=unused;Database=mimir;Username=test",
            ["ConnectionStrings:rabbitmq"] = "amqp://unused"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddGnaInfrastructure(configuration);
        services.AddMimirInfrastructure(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
