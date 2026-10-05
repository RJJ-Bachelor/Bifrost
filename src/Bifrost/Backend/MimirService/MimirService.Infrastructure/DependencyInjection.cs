using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MimirService.Application.Messaging;
using MimirService.Infrastructure.Messaging;
using Shared.Application.Messaging;
using Shared.Infrastructure.Persistence.Messaging;

namespace MimirService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMimirInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddRabbitMqMessageBus(configuration);
        services.AddScoped<IRequestMessageHandler, RequestMessageHandler>();
        services.AddScoped<IGeneralizedMessagePublisher, GeneralizedMessagePublisher>();
        services.AddHostedService<RequestMessageSubscriber>();

        return services;
    }
}
