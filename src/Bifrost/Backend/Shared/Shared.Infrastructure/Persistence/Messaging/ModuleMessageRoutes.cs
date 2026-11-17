using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Persistence.Messaging;

public sealed record ModuleMessageRegistration(string Route, string Queue, string Exchange, string RoutingKey,
    bool UseRabbitMq, Func<IServiceProvider, MessageEnvelope<JsonElement>, CancellationToken, Task> DispatchAsync);

public static class ModuleMessageServiceCollectionExtensions
{
    public static IServiceCollection AddModuleMessageRoute<T>(this IServiceCollection services, string route,
        string queue, string exchange, string routingKey, IConfiguration configuration)
    {
        if (services.Any(item => item.ImplementationInstance is ModuleMessageRegistration existing && existing.Route == route)) return services;
        var transport = configuration[$"Messaging:ModuleRoutes:{route}:Transport"] ?? "Local";
        if (transport is not ("Local" or "RabbitMq"))
            throw new InvalidOperationException($"Unknown transport '{transport}' for module route '{route}'.");
        var rabbit = transport == "RabbitMq";
        services.AddSingleton(new ModuleMessageRegistration(route, queue, exchange, routingKey, rabbit,
            async (provider, message, token) =>
            {
                if (rabbit)
                    await provider.GetRequiredService<IMessageBus>().PublishEnvelopeAsync(
                        new(message.Id, message.Payload, message.Metadata), exchange, routingKey, token);
                else
                    await provider.GetRequiredService<IRawMessageReceiver<T>>().ReceiveAsync(message, token);
            }));
        if (rabbit) services.AddRabbitMqQueue(queue, exchange, routingKey);
        return services;
    }

    public static IServiceCollection AddModuleMessageSubscriber<T>(this IServiceCollection services, string route)
    {
        services.AddSingleton<IHostedService>(provider => new ModuleMessageSubscriber<T>(
            provider.GetRequiredService<IMessageBus>(), provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetServices<ModuleMessageRegistration>().Single(item => item.Route == route)));
        return services;
    }
}

internal sealed class ModuleMessageSubscriber<T>(IMessageBus bus, IServiceScopeFactory scopes,
    ModuleMessageRegistration route) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!route.UseRabbitMq) return;
        await using var subscription = await bus.SubscribeAsync<JsonElement>(route.Queue, route.Exchange,
            route.RoutingKey, async (message, token) =>
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IRawMessageReceiver<T>>().ReceiveAsync(message, token);
            }, stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
