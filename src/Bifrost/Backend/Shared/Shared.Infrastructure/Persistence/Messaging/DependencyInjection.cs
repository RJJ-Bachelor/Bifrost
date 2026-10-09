using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Persistence.Messaging;

public static class DependencyInjection
{
    public static IServiceCollection AddRabbitMqMessageBus(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IMessageBus, RabbitMqMessageBus>();
        return services;
    }
}
