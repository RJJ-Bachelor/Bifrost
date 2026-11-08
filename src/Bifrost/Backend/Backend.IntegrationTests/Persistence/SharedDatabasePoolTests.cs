using System.Data.Common;
using EirService.HelpRequests.Infrastructure;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.KnowledgeBase.Infrastructure;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using EirService.Participants.Infrastructure;
using EirService.Participants.Infrastructure.Persistence;
using EirService.Sessions.Infrastructure;
using EirService.Sessions.Infrastructure.Persistence;
using GnaService.Infrastructure;
using GnaService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Shared.Infrastructure.Persistence;

namespace Backend.IntegrationTests.Persistence;

// These tests inspect real provider options and DI ownership without opening database connections.
public sealed class SharedDatabasePoolTests
{
    [Fact]
    public async Task AllFourEirModulesShareOnePoolWithAFortyConnectionDefault()
    {
        var services = BuildServices();
        Assert.Single(services, service => service.IsKeyedService &&
            service.ServiceType == typeof(DbDataSource) && Equals(service.ServiceKey, "eir"));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var source = Assert.IsAssignableFrom<NpgsqlDataSource>(provider.GetRequiredKeyedService<DbDataSource>("eir"));
        Assert.Equal(40, new NpgsqlConnectionStringBuilder(source.ConnectionString).MaxPoolSize);

        await using var scope = provider.CreateAsyncScope();
        DbContext[] contexts = [
            scope.ServiceProvider.GetRequiredService<EirRequestDbContext>(),
            scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>(),
            scope.ServiceProvider.GetRequiredService<EirSessionDbContext>(),
            scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>()
        ];
        Assert.All(contexts, context => Assert.Same(source, ConfiguredDataSource(context)));
        AssertMigrationHistory(contexts[0], "__HelpRequestsMigrationsHistory", null);
        AssertMigrationHistory(contexts[1], "__EFMigrationsHistory", "participants");
        AssertMigrationHistory(contexts[2], "__EFMigrationsHistory", "sessions");
        AssertMigrationHistory(contexts[3], "__EFMigrationsHistory", "knowledgebase");
    }

    [Theory]
    [InlineData("Maximum Pool Size=13", 13)]
    [InlineData("MaxPoolSize=27", 27)]
    [InlineData("Maximum Pool Size=100", 100)]
    public async Task ExplicitPoolLimitAndOtherConnectionSettingsArePreserved(string poolSetting, int expectedLimit)
    {
        var services = BuildServices("Host=unused;Database=eir;Username=test;Timeout=9;Application Name=EirPoolTest;" + poolSetting);
        await using var provider = services.BuildServiceProvider();
        var source = provider.GetRequiredKeyedService<DbDataSource>("eir");
        var options = new NpgsqlConnectionStringBuilder(source.ConnectionString);

        Assert.Equal(expectedLimit, options.MaxPoolSize);
        Assert.Equal(9, options.Timeout);
        Assert.Equal("EirPoolTest", options.ApplicationName);
    }

    [Fact]
    public async Task ASecondNamedDatabaseHasAnIndependentPoolAndDoesNotChangeGnaOptions()
    {
        var services = BuildServices();
        services.AddSharedPostgresDataSource("another-database",
            new NpgsqlConnectionStringBuilder("Host=unused;Database=another;Username=test;Maximum Pool Size=7"),
            NpgsqlDataSource.Create);
        await using var provider = services.BuildServiceProvider();
        var eir = provider.GetRequiredKeyedService<DbDataSource>("eir");
        var another = provider.GetRequiredKeyedService<DbDataSource>("another-database");

        Assert.NotSame(eir, another);
        Assert.Equal(40, new NpgsqlConnectionStringBuilder(eir.ConnectionString).MaxPoolSize);
        Assert.Equal(7, new NpgsqlConnectionStringBuilder(another.ConnectionString).MaxPoolSize);
        Assert.Null(provider.GetService<DbDataSource>());
        await using var scope = provider.CreateAsyncScope();
        var gna = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
        Assert.Null(ConfiguredDataSource(gna));
        var gnaOptions = new NpgsqlConnectionStringBuilder(gna.Database.GetConnectionString());
        Assert.Equal("gna", gnaOptions.Database);
        Assert.Equal(17, gnaOptions.MaxPoolSize);
    }

    private static ServiceCollection BuildServices(string eirConnectionString = "Host=unused;Database=eir;Username=test")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:eir"] = eirConnectionString,
            ["ConnectionStrings:gna"] = "Host=unused;Database=gna;Username=test;Maximum Pool Size=17",
            ["ConnectionStrings:rabbitmq"] = "amqp://unused"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddHelpRequestsInfrastructure(configuration);
        services.AddParticipantsInfrastructure(configuration);
        services.AddSessionsInfrastructure(configuration);
        services.AddKnowledgeBaseInfrastructure(configuration);
        services.AddGnaInfrastructure(configuration);
        return services;
    }

    private static DbDataSource? ConfiguredDataSource(DbContext context)
    {
        // Npgsql currently exposes the supplied data source on its provider options extension.
#pragma warning disable EF1001
        return context.GetService<IDbContextOptions>().FindExtension<NpgsqlOptionsExtension>()?.DataSource;
#pragma warning restore EF1001
    }

    private static void AssertMigrationHistory(DbContext context, string table, string? schema)
    {
        var options = Assert.Single(context.GetService<IDbContextOptions>().Extensions.OfType<RelationalOptionsExtension>());
        Assert.Equal(table, options.MigrationsHistoryTableName);
        Assert.Equal(schema, options.MigrationsHistoryTableSchema);
    }
}
