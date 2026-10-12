using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MimirService.Application.Repositories;
using MimirService.Infrastructure.Messaging;
using MimirService.Infrastructure.Persistence;
using MimirService.Infrastructure.Repositories;
using Shared.Application.Messaging;
using Shared.Infrastructure.Persistence.Messaging;
using Shared.Infrastructure.Persistence.Outbox;
using MimirService.Application.Services.Messaging;
using MimirService.Application.Features.Internal.SessionUpdated;

namespace MimirService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMimirInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("mimir");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The 'mimir' PostgreSQL connection string is not configured.");
        }

        services.AddDbContext<MimirRequestDbContext>(options =>
            options.UseNpgsql(connectionString));
        services.AddRabbitMqMessageBus(configuration);
        services.AddOutbox<MimirRequestDbContext>();
        services.AddScoped<IHelpRequestMessageHandler, HelpRequestMessageHandler>();
        services.AddScoped<IHelpRequestRepository, HelpRequestRepository>();
        services.AddHostedService<HelpRequestMessageSubscriber>();
        services.AddScoped<ISessionUpdatedMessageHandler, SessionUpdatedMessageHandler>();
        services.AddHostedService<SessionUpdatedMessageSubscriber>();

        return services;
    }
}
