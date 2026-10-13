using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Persistence.Messaging;

public static class DependencyInjection
{
    public static IServiceCollection AddRabbitMqQueue(
        this IServiceCollection services, string queueName, string exchangeName, string routingKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(exchangeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);
        services.AddSingleton<IHostedService>(provider => new RabbitMqQueueInitializer(
            provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<ILogger<RabbitMqQueueInitializer>>(),
            queueName, exchangeName, routingKey));
        return services;
    }

    public static IServiceCollection AddRabbitMqMessageBus(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton<IMessageBus, RabbitMqMessageBus>();
        return services;
    }
}
