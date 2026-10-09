using GnaService.Application.Repositories;
using GnaService.Application.Services.Messaging;
using GnaService.Infrastructure.Messaging;
using GnaService.Infrastructure.Persistence;
using GnaService.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Persistence.Messaging;

namespace GnaService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGnaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("gna");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The 'gna' PostgreSQL connection string is not configured.");
        }

        services.AddDbContext<GnaNotificationDbContext>(options =>
            options.UseNpgsql(connectionString));
        services.AddRabbitMqMessageBus(configuration);
        services.AddScoped<INotificationMessageHandler, NotificationMessageHandler>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddHostedService<NotificationMessageSubscriber>();

        return services;
    }
}
