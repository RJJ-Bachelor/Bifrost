using EirService.KnowledgeBase.Application.Features.Internal.FaqAnswered;
using EirService.KnowledgeBase.Application.Features.Internal.RegisterGeneralization;
using Microsoft.Extensions.DependencyInjection;

namespace EirService.KnowledgeBase.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddKnowledgeBaseApplication(this IServiceCollection services)
    {
        services.AddScoped<RegisterGeneralizationMessageHandler>();
        services.AddScoped<FaqAnsweredMessageHandler>();
        return services;
    }
}
