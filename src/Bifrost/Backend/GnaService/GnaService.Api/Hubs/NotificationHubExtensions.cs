using GnaService.Application.Notifications;
using GnaService.Infrastructure.Notifications;
using Microsoft.AspNetCore.SignalR;
using Shared.Api.Authentication;

namespace GnaService.Api.Hubs;

public static class NotificationHubExtensions
{
    public static IServiceCollection AddNotificationRealtime(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<IUserIdProvider, NotificationUserIdProvider>();
        services.AddScoped<IRealtimeNotificationSender, SignalRNotificationSender<NotificationHub>>();
        return services;
    }

    public static IEndpointRouteBuilder MapNotificationHubs(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<NotificationHub>(NotificationHubAuthentication.StudentPath,
            options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization("Student");
        endpoints.MapHub<NotificationHub>(NotificationHubAuthentication.TeacherPath,
            options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization("Teacher");
        return endpoints;
    }
}
