using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MimirService.Application.Repositories;
using MimirService.Infrastructure.Messaging;
using MimirService.Infrastructure.Persistence;
using MimirService.Infrastructure.Repositories;
using Shared.Application.Messaging;
using Shared.Infrastructure.Persistence.Messaging;
using MimirService.Application.Services.Messaging;

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
        services.AddScoped<IRequestMessageHandler, RequestMessageHandler>();
        services.AddScoped<IGeneralizedMessagePublisher, GeneralizedMessagePublisher>();
        services.AddScoped<IMimirRequestRepository, MimirRequestRepository>();
        services.AddHostedService<RequestMessageSubscriber>();

        return services;
    }
}
