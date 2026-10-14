using Shared.Api.Authentication;
using GnaService.Infrastructure;
using GnaService.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Migrations;
using FastEndpoints;
using GnaService.Api.Hubs;
using GnaService.Application.Services.Messaging;
using Microsoft.AspNetCore.HttpOverrides;

namespace GnaService.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddGnaInfrastructure(builder.Configuration);

        // Add services to the container.
        builder.Services.AddBifrostServiceAuthentication(builder.Configuration, builder.Environment);
        builder.Services.AddNotificationRealtime();
        builder.Services.AddMessaging(typeof(NotificationMessageHandler).Assembly);

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        var app = builder.Build();
        app.Services.UseMessaging();
        app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto });

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<GnaNotificationDbContext>();
            dbContext.Database.MigrateAtStartup();
        }

        app.MapDefaultEndpoints();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapNotificationHubs();

        var summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        app.MapGet("/weatherforecast", (HttpContext httpContext) =>
        {
            var forecast =  Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                {
                    Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    TemperatureC = Random.Shared.Next(-20, 55),
                    Summary = summaries[Random.Shared.Next(summaries.Length)]
                })
                .ToArray();
            return forecast;
        })
        .WithName("GetWeatherForecast");

        app.Run();
    }
}
