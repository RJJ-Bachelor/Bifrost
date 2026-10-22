using EirService.HelpRequests.Application.Features.Internal.SessionUpdated;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Infrastructure.Messaging;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.HelpRequests.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Persistence;
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

        services.AddDbContext<EirRequestDbContext>(options =>
            options.UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__HelpRequestsMigrationsHistory")));
        services.AddRabbitMqMessageBus(configuration);
        services.AddOutbox<EirRequestDbContext>(TimeSpan.FromMilliseconds(500));
        services.AddScoped<IHelpRequestRepository, HelpRequestRepository>();
        services.AddScoped<IUnitOfWork<IHelpRequestRepository>, HelpRequestUnitOfWork>();
        services.AddScoped<ISessionUpdatedMessageHandler, SessionUpdatedMessageHandler>();
        services.AddHostedService<SessionUpdatedMessageSubscriber>();

        return services;
    }
}
