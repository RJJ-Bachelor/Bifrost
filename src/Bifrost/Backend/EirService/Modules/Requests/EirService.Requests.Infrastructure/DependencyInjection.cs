using EirService.Requests.Application.Repositories;
using EirService.Requests.Infrastructure.Messaging;
using EirService.Requests.Infrastructure.Persistence;
using EirService.Requests.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Infrastructure.Persistence.Messaging;
using EirService.Requests.Application.Services.Messaging;

namespace EirService.Requests.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRequestsInfrastructure(
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
            options.UseNpgsql(connectionString));
        services.AddRabbitMqMessageBus(configuration);
        services.AddScoped<IRequestMessagePublisher, RequestMessagePublisher>();
        services.AddScoped<INotificationMessagePublisher, NotificationMessagePublisher>();
        services.AddScoped<IGeneralizedMessageHandler, GeneralizedMessageHandler>();
        services.AddScoped<IRequestRepository, RequestRepository>();
        services.AddHostedService<GeneralizedMessageSubscriber>();

        return services;
    }
}
