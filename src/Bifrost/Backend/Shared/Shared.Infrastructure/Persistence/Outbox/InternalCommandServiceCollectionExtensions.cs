using System.Text.Json;
using FastEndpoints;
using Shared.Application.Messaging;
using Shared.Infrastructure.Persistence.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Infrastructure.Persistence.Outbox;

public static class InternalCommandServiceCollectionExtensions
{
    public static IServiceCollection AddLegacyModuleCommand<TCommand, TMessage>(this IServiceCollection services,
        string route, Func<TCommand, TMessage> convert) where TCommand : class, ICommand
    {
        services.AddSingleton(new InternalCommandRegistration(route, typeof(TCommand), async (provider, message, token) =>
        {
            var command = message.Payload.Deserialize<TCommand>(JsonOptions)
                ?? throw new InvalidDataException("The legacy command payload is missing.");
            var contract = MessageContracts.For(typeof(TMessage));
            var metadata = message.Metadata is null ? null : message.Metadata with { Type = contract.Name, Version = contract.Version };
            await provider.GetRequiredService<IMessageReceiver<TMessage>>().ReceiveAsync(
                new(message.Id, convert(command), metadata), token);
        }));
        return services;
    }

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

        services.AddSingleton(new InternalCommandRegistration(routingKey, typeof(TCommand), async (provider, message, token) =>
        {
            var command = message.Payload.Deserialize<TCommand>(JsonOptions)
                ?? throw new InvalidDataException($"The internal command for '{routingKey}' is missing.");
            await provider.GetRequiredService<ICommandHandler<TCommand>>().ExecuteAsync(command, token);
        }));
        return services;
    }
}

internal sealed record InternalCommandRegistration(
    string RoutingKey,
    Type CommandType,
    Func<IServiceProvider, MessageEnvelope<JsonElement>, CancellationToken, Task> DispatchAsync);
