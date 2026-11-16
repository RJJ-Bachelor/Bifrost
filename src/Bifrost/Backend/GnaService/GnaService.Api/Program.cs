using Shared.Api.Authentication;
using GnaService.Api.Hubs;
using GnaService.Api.Middleware;
using FastEndpoints;
using GnaService.Application.Services.Messaging;
using GnaService.Infrastructure;
using GnaService.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Shared.Infrastructure.Persistence.Migrations;

namespace GnaService.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddGnaInfrastructure(builder.Configuration);
        builder.Services.AddBifrostServiceAuthentication(builder.Configuration, builder.Environment);
        builder.Services.AddNotificationRealtime();
        builder.Services.AddMessaging(typeof(NotificationMessageHandler).Assembly);
        builder.Services.AddPushNotificationApi();

        var app = builder.Build();
        app.Services.UseMessaging();
        app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto });

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
            dbContext.Database.MigrateAtStartup();
        }

        app.MapDefaultEndpoints();
        app.UseMiddleware<ValidationExceptionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseFastEndpoints(configuration => configuration.Endpoints.RoutePrefix = "api");
        app.MapNotificationHubs();

        app.Run();
    }
}
