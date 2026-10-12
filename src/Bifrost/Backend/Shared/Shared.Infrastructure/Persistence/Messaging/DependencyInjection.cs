using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Persistence.Messaging;

public static class DependencyInjection
{
    public static IServiceCollection AddRabbitMqMessageBus(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton<IMessageBus, RabbitMqMessageBus>();
        return services;
    }
}
