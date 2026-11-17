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
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging;

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
        services.AddModuleMessageRoute<SessionUpdatedIntegrationEvent>(EirModuleRoutes.SessionUpdated,
            "eir-helprequests-session-updated", "eir.modules", "session.updated", configuration);
        services.AddModuleMessageRoute<FaqAnsweredIntegrationEvent>(EirModuleRoutes.FaqAnswerRecorded,
            "eir-knowledgebase-faq-answered", "eir.modules", "faq.answered", configuration);
        services.AddMessageReceiver<SessionUpdatedIntegrationEvent, EirRequestDbContext, SessionUpdatedMessageHandler>(
            "eir.helprequests.session-updated", maximumAttempts: configuration.GetValue("Messaging:Delivery:MaximumAttempts", 8));
        services.AddModuleMessageSubscriber<SessionUpdatedIntegrationEvent>(EirModuleRoutes.SessionUpdated);
        services.AddLegacyModuleCommand<HandleSessionChangeCommand, SessionUpdatedIntegrationEvent>(
            EirModuleRoutes.SessionChanged, command => new(command.EventId, command.SessionId, command.Generation,
                (SessionUpdateType)command.EventType, command.UpdatedAtUtc, command.PreviousGeneration));

        return services;
    }
}
