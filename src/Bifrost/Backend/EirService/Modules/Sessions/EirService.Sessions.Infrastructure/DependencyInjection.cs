using EirService.Sessions.Application.Repositories;
using EirService.Sessions.Infrastructure.Persistence;
using EirService.Sessions.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Infrastructure.Persistence.Messaging;
using Shared.Infrastructure.Persistence.Outbox;
using Shared.Infrastructure.Persistence.UnitOfWork;

namespace EirService.Sessions.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSessionsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("eir")
            ?? throw new InvalidOperationException("The 'eir' PostgreSQL connection string is not configured.");
        services.AddDbContext<EirSessionDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "sessions")));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddUnitOfWork<ISessionRepository, EirSessionDbContext>();
        services.AddRabbitMqMessageBus(configuration);
        services.AddOutbox<EirSessionDbContext>();
        return services;
    }
}
