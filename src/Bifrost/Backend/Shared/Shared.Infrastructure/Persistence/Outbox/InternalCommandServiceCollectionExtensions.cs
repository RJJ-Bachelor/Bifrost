using System.Text.Json;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Infrastructure.Persistence.Outbox;

public static class InternalCommandServiceCollectionExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IServiceCollection AddInternalCommand<TCommand>(this IServiceCollection services, string routingKey)
        where TCommand : class, ICommand
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);
        var existing = services.Select(descriptor => descriptor.ImplementationInstance)
            .OfType<InternalCommandRegistration>().SingleOrDefault(registration => registration.RoutingKey == routingKey);
        if (existing is not null)
        {
            if (existing.CommandType != typeof(TCommand))
                throw new InvalidOperationException($"Internal route '{routingKey}' is already registered for another command.");
            return services;
        }

        services.AddSingleton(new InternalCommandRegistration(routingKey, typeof(TCommand), async (provider, payload, token) =>
        {
            var command = JsonSerializer.Deserialize<TCommand>(payload, JsonOptions)
                ?? throw new InvalidDataException($"The internal command for '{routingKey}' is missing.");
            await provider.GetRequiredService<ICommandHandler<TCommand>>().ExecuteAsync(command, token);
        }));
        return services;
    }
}

internal sealed record InternalCommandRegistration(
    string RoutingKey,
    Type CommandType,
    Func<IServiceProvider, string, CancellationToken, Task> DispatchAsync);
