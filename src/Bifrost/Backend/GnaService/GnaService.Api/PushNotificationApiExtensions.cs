using FastEndpoints;
using FluentValidation;
using GnaService.Api.Middleware.Commands;
using GnaService.Application.Features.Common.RegisterPushSubscription;
using GnaService.Application.Features.Common.UnregisterPushSubscription;

namespace GnaService.Api;

public static class PushNotificationApiExtensions
{
    public static IServiceCollection AddPushNotificationApi(this IServiceCollection services)
    {
        services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(Program).Assembly, typeof(RegisterPushSubscriptionCommandHandler).Assembly];
        });
        services.AddTransient<IValidator<RegisterPushSubscriptionCommand>, RegisterPushSubscriptionCommandValidator>();
        services.AddTransient<IValidator<UnregisterPushSubscriptionCommand>, UnregisterPushSubscriptionCommandValidator>();
        services.AddCommandMiddleware(options => options.Register(typeof(CommandValidationMiddleware<,>)));
        return services;
    }
}
