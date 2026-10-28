using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MimirService.Application.Repositories;
using MimirService.Application.Services.Generalizations;
using MimirService.Application.Services.Llm;
using MimirService.Infrastructure.Llm;
using MimirService.Infrastructure.Messaging;
using MimirService.Infrastructure.Persistence;
using MimirService.Infrastructure.Repositories;
using Shared.Application.Messaging;
using Shared.Infrastructure.Persistence.Messaging;
using Shared.Infrastructure.Persistence.Outbox;
using MimirService.Application.Services.Messaging;

namespace MimirService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMimirInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("mimir");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The 'mimir' PostgreSQL connection string is not configured.");
        }

        services.AddDbContext<MimirRequestDbContext>(options =>
            options.UseNpgsql(connectionString));
        services.AddRabbitMqMessageBus(configuration);
        services.AddRabbitMqQueue("mimir-request-answered", MessageRoutes.Requests, MessageRoutes.HelpRequestAnswered);
        services.AddOutbox<MimirRequestDbContext>();
        services.AddScoped<IHelpRequestMessageHandler, HelpRequestMessageHandler>();
        services.AddScoped<IGeneralizationDecider, PlaceholderGeneralizationDecider>();
        services.AddScoped<IHelpRequestRepository, HelpRequestRepository>();
        services.AddScoped<IGeneralizedMessagePublisher, OutboxGeneralizedMessagePublisher>();
        services.AddHostedService<HelpRequestMessageSubscriber>();
        services.AddScoped<ISessionUpdatedMessageHandler, SessionUpdatedMessageHandler>();
        services.AddHostedService<SessionUpdatedMessageSubscriber>();

        services.AddOptions<GeminiOptions>()
            .Bind(configuration.GetSection(GeminiOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Gemini:ApiKey is not configured.")
            .Validate(o => o.Models.Length > 0, "Gemini:Models must contain at least one model.");

#pragma warning disable EXTEXP0001
        services.AddHttpClient<ILlmClient, GeminiLlmClient>(client =>
            {
                client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        return services;
    }
}
