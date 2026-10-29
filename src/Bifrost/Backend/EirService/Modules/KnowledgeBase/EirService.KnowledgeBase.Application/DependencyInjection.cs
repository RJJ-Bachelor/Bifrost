using EirService.Contracts.KnowledgeBase.GetArchivedFaqs;
using EirService.Contracts.KnowledgeBase.GetArchivedFaqCounts;
using EirService.KnowledgeBase.Application.Features.Internal.GetArchivedFaqCounts;
using EirService.KnowledgeBase.Application.Features.Internal.FaqAnswered;
using EirService.KnowledgeBase.Application.Features.Internal.GetArchivedFaqs;
using EirService.KnowledgeBase.Application.Features.Internal.RegisterGeneralization;
using EirService.KnowledgeBase.Application.Features.Teacher.CreateFaq;
using FastEndpoints;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace EirService.KnowledgeBase.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddKnowledgeBaseApplication(this IServiceCollection services)
    {
        services.AddScoped<RegisterGeneralizationMessageHandler>();
        services.AddScoped<FaqAnsweredMessageHandler>();
        services.AddTransient<IValidator<CreateFaqCommand>, CreateFaqCommandValidator>();
        services.AddScoped<ICommandHandler<GetArchivedFaqsQuery, IReadOnlyList<ArchivedFaq>>, GetArchivedFaqsQueryHandler>();
        services.AddScoped<ICommandHandler<GetArchivedFaqCountsQuery, IReadOnlyDictionary<int, int>>, GetArchivedFaqCountsQueryHandler>();
        return services;
    }
}
