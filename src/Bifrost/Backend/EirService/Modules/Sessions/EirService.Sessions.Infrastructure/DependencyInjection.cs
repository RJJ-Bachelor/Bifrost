using EirService.Contracts.Sessions.GetSessionTeacherNames;
using EirService.Sessions.Application.Features.Internal.GetSessionTeacherNames;
using EirService.Sessions.Application.Repositories;
using EirService.Contracts.Sessions.GetHelpRequestSession;
using EirService.Contracts.Sessions.GetSessionGenerations;
using EirService.Sessions.Application.Features.Internal.GetHelpRequestSession;
using EirService.Sessions.Application.Features.Internal.GetSessionGenerations;
using FastEndpoints;
using EirService.Sessions.Infrastructure.Persistence;
using EirService.Sessions.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Infrastructure.Persistence.Messaging;
using Shared.Infrastructure.Persistence.Outbox;
using Shared.Application.Persistence;
using Shared.Application.Messaging;
using Npgsql;
using Shared.Infrastructure.Persistence;
using System.Data.Common;

namespace EirService.Sessions.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSessionsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("eir")
            ?? throw new InvalidOperationException("The 'eir' PostgreSQL connection string is not configured.");
        services.AddSharedPostgresDataSource("eir", new NpgsqlConnectionStringBuilder(connectionString), NpgsqlDataSource.Create);
        services.AddDbContext<EirSessionDbContext>((provider, options) => options.UseNpgsql(provider.GetRequiredKeyedService<DbDataSource>("eir"),
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "sessions")));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<ICommandHandler<GetHelpRequestSessionQuery, HelpRequestSession?>, GetHelpRequestSessionQueryHandler>();
        services.AddScoped<ICommandHandler<GetSessionTeacherNamesQuery, IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string?>>>, GetSessionTeacherNamesQueryHandler>();
        services.AddScoped<ICommandHandler<GetSessionGenerationsQuery, IReadOnlyDictionary<Guid, int>>, GetSessionGenerationsQueryHandler>();
        services.AddScoped<IUnitOfWork<ISessionRepository>, SessionUnitOfWork>();
        services.AddRabbitMqMessageBus(configuration);
        services.AddRabbitMqQueue("mimir-session-updated", MessageRoutes.Sessions, MessageRoutes.SessionUpdated);
        services.AddOutbox<EirSessionDbContext>();
        services.AddInternalOutbox<EirSessionDbContext>();
        return services;
    }
}
