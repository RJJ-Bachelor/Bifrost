using EirService.Requests.Application.Repositories;
using EirService.Requests.Infrastructure.Persistence;
using EirService.Requests.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EirService.Requests.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRequestsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("bifrost");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The 'bifrost' PostgreSQL connection string is not configured.");
        }

        services.AddDbContext<EirRequestDbContext>(options =>
            options.UseNpgsql(connectionString));
        services.AddScoped<IRequestRepository, RequestRepository>();

        return services;
    }
}
