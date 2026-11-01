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
        services.AddGeneralizationDecider(configuration);
        services.AddScoped<IHelpRequestRepository, HelpRequestRepository>();
        services.AddScoped<IGeneralizedMessagePublisher, OutboxGeneralizedMessagePublisher>();
        services.AddHostedService<HelpRequestMessageSubscriber>();
        services.AddScoped<ISessionUpdatedMessageHandler, SessionUpdatedMessageHandler>();
        services.AddHostedService<SessionUpdatedMessageSubscriber>();
        services.AddGeminiLlmClient(configuration);

        return services;
    }

    // Fake is the default so Mimir runs without an LLM
    private static void AddGeneralizationDecider(this IServiceCollection services, IConfiguration configuration)
    {
        var decider = configuration["Generalization:Decider"] ?? "Fake";

        if (decider.Equals("Fake", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IGeneralizationDecider, PlaceholderGeneralizationDecider>();
        else if (decider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IGeneralizationDecider, GeneralizationDecider>();
        else
            throw new InvalidOperationException(
                $"Generalization:Decider must be 'Fake' or 'Gemini', but was '{decider}'.");
    }

    // Separate from AddMimirInfrastructure so the eval harness can use Gemini without a database or broker.
    public static IServiceCollection AddGeminiLlmClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
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
