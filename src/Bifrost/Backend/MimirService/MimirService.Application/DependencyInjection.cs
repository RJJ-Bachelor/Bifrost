using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MimirService.Application.Features.Development.GeneralizeHelpRequests;

namespace MimirService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddMimirDevelopmentFeatures(this IServiceCollection services)
    {
        services.AddTransient<IValidator<GeneralizeHelpRequestsCommand>, GeneralizeHelpRequestsCommandValidator>();
        services.AddScoped<GeneralizeHelpRequestsCommandHandler>();
        return services;
    }
}
