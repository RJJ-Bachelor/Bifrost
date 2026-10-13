using EirService.HelpRequests.Application.Features.Common;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace EirService.HelpRequests.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddHelpRequestsApplication(this IServiceCollection services)
    {
        services.AddScoped<HelpRequestAccess>();
        foreach (var type in typeof(DependencyInjection).Assembly.GetTypes().Where(type => !type.IsAbstract))
            foreach (var contract in type.GetInterfaces().Where(contract => contract.IsGenericType &&
                contract.GetGenericTypeDefinition() == typeof(IValidator<>)))
                services.AddTransient(contract, type);
        return services;
    }
}
