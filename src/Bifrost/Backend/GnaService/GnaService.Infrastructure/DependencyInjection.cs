using GnaService.Application.Repositories;
using GnaService.Application.Services.Messaging;
using GnaService.Infrastructure.Messaging;
using GnaService.Infrastructure.Persistence;
using GnaService.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Infrastructure.Persistence.Messaging;
using GnaService.Application.Notifications;
using GnaService.Infrastructure.Notifications;
using GnaService.Infrastructure.Push;
using Shared.Application.Persistence;
using Shared.Infrastructure.Persistence.Outbox;

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
        services.AddRabbitMqQueue("gna-request-updated", MessageRoutes.Requests, MessageRoutes.HelpRequestUpdated);
        services.AddScoped<INotificationMessageHandler, NotificationMessageHandler>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IUnitOfWork<INotificationRepository>, NotificationUnitOfWork>();
        services.AddOutbox<GnaNotificationDbContext>(TimeSpan.FromMilliseconds(500));
        services.AddSingleton<IConnectionRegistry, ConnectionRegistry>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<IPushNotificationSender, PushNotificationSender>();
        services.AddScoped<IPushSubscriptionRepository, PushSubscriptionRepository>();
        services.AddHostedService<NotificationMessageSubscriber>();
        services.AddHostedService<NotificationDeliverySubscriber>();

        return services;
    }
}
