using EirService.Insights.Application.Features.Teacher.GetSessionStatistics;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace EirService.Insights.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddInsightsApplication(this IServiceCollection services)
    {
        services.AddTransient<IValidator<GetSessionStatisticsQuery>, GetSessionStatisticsQueryValidator>();
        return services;
    }
}
