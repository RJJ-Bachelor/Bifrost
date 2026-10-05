using GnaService.Application.Messaging;
using GnaService.Infrastructure.Messaging;
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
        services.AddRabbitMqMessageBus(configuration);
        services.AddScoped<INotificationMessageHandler, NotificationMessageHandler>();
        services.AddHostedService<NotificationMessageSubscriber>();

        return services;
    }
}
