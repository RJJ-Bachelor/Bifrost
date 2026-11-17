using GnaService.Application.Repositories;
using GnaService.Application.Services.Messaging;
using GnaService.Infrastructure.Messaging;
using GnaService.Infrastructure.Persistence;
using GnaService.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Shared.Application.Messaging;
using Shared.Infrastructure.Persistence.Messaging;
using GnaService.Application.Notifications;
using GnaService.Infrastructure.Notifications;
using GnaService.Infrastructure.Push;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Shared.Application.Persistence;
using GnaService.Infrastructure.Persistence.Outbox;

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

        var maximumPoolSize = configuration.GetValue("Database:MaximumPoolSize", 40);
        var databaseConnection = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = maximumPoolSize
        }.ConnectionString;
        services.AddDbContext<GnaNotificationDbContext>(options =>
            options.UseNpgsql(databaseConnection, postgres => postgres.EnableRetryOnFailure(
                maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(2), errorCodesToAdd: null)));
        services.AddRabbitMqMessageBus(configuration);
        services.AddRabbitMqQueue(MessageRoutes.NotificationCreated, MessageRoutes.Notifications, MessageRoutes.NotificationCreated);
        services.AddRabbitMqQueue(MessageRoutes.SessionChanged, MessageRoutes.Notifications, MessageRoutes.SessionChanged);
        services.AddRabbitMqQueue("gna-request-updated", MessageRoutes.Requests, MessageRoutes.HelpRequestUpdated);
        services.AddScoped<INotificationMessageHandler, NotificationMessageHandler>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IUnitOfWork<INotificationRepository>, NotificationUnitOfWork>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<NotificationOutboxDispatcher>();
        services.AddSingleton<IConnectionRegistry, ConnectionRegistry>();
        services.AddScoped<NotificationDispatcher>();
        services.AddOptions<WebPushOptions>()
            .Bind(configuration.GetSection(WebPushOptions.SectionName))
            .Validate(options => options.IsValid(), "Push requires a contact URI and a valid matching VAPID key pair when enabled.")
            .ValidateOnStart();
        services.AddMemoryCache();
        services.AddSingleton<IVapidTokenCache, VapidTokenCache>();
        services.AddHttpClient("WebPush", client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .AddTypedClient(client => new PushServiceClient(client) { AutoRetryAfter = false });
        services.AddScoped<IPushNotificationSender, PushNotificationSender>();
        services.AddScoped<IPushSubscriptionRepository, PushSubscriptionRepository>();
        services.AddHostedService<NotificationMessageSubscriber>();
        services.AddHostedService<SessionChangedSubscriber>();

        return services;
    }
}
