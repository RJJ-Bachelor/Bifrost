using EirService.Contracts.HelpRequests.HandleSessionChange;
using EirService.Contracts.Messaging;
using EirService.HelpRequests.Application.Features.Internal.HandleSessionChange;
using FastEndpoints;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.HelpRequests.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shared.Application.Persistence;
using Shared.Infrastructure.Persistence;
using System.Data.Common;
using Shared.Infrastructure.Persistence.Messaging;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.HelpRequests.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddHelpRequestsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("eir");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The 'eir' PostgreSQL connection string is not configured.");
        }

        var maximumPoolSize = configuration.GetValue("Database:MaximumPoolSize", 40);
        services.AddSharedPostgresDataSource("eir", new NpgsqlConnectionStringBuilder(connectionString),
            NpgsqlDataSource.Create, maximumPoolSize);
        services.AddDbContext<EirRequestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredKeyedService<DbDataSource>("eir"),
                postgres => postgres.MigrationsHistoryTable("__HelpRequestsMigrationsHistory")));
        services.AddRabbitMqMessageBus(configuration);
        services.AddOutbox<EirRequestDbContext>(TimeSpan.FromMilliseconds(500));
        services.AddInternalOutbox<EirRequestDbContext>(TimeSpan.FromMilliseconds(500));
        services.AddScoped<IHelpRequestRepository, HelpRequestRepository>();
        services.AddScoped<IArchivedConversationRepository, ArchivedConversationRepository>();
        services.AddScoped<IUnitOfWork<IHelpRequestRepository>, HelpRequestUnitOfWork>();
        services.AddScoped<ICommandHandler<HandleSessionChangeCommand>, HandleSessionChangeCommandHandler>();
        services.AddInternalCommand<HandleSessionChangeCommand>(EirModuleRoutes.SessionChanged);

        return services;
    }
}
