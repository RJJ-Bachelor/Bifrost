using EirService.Contracts.HelpRequests.ApplyGeneralization;
using EirService.Contracts.HelpRequests.CanApplyGeneralization;
using EirService.Contracts.HelpRequests.GetArchivedConversationCounts;
using EirService.Contracts.HelpRequests.GetArchivedConversations;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Features.Internal.ApplyGeneralization;
using EirService.HelpRequests.Application.Features.Internal.CanApplyGeneralization;
using EirService.HelpRequests.Application.Features.Internal.GetArchivedConversationCounts;
using EirService.HelpRequests.Application.Features.Internal.GetArchivedConversations;
using FastEndpoints;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace EirService.HelpRequests.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddHelpRequestsApplication(this IServiceCollection services)
    {
        services.AddScoped<HelpRequestAccess>();
        services.AddScoped<ICommandHandler<GetArchivedConversationCountsQuery, IReadOnlyDictionary<int, int>>, GetArchivedConversationCountsQueryHandler>();
        services.AddScoped<ICommandHandler<GetArchivedConversationsQuery, IReadOnlyList<ArchivedConversation>>, GetArchivedConversationsQueryHandler>();
        services.AddScoped<ICommandHandler<CanApplyGeneralizationQuery, bool>, CanApplyGeneralizationQueryHandler>();
        services.AddScoped<ICommandHandler<ApplyGeneralizationCommand, bool>, ApplyGeneralizationCommandHandler>();
        foreach (var type in typeof(DependencyInjection).Assembly.GetTypes().Where(type => !type.IsAbstract))
            foreach (var contract in type.GetInterfaces().Where(contract => contract.IsGenericType &&
                contract.GetGenericTypeDefinition() == typeof(IValidator<>)))
                services.AddTransient(contract, type);
        return services;
    }
}
