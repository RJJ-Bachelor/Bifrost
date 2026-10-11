using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Shared.Infrastructure.Persistence.Outbox;

public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddOutbox<TDbContext>(this IServiceCollection services) where TDbContext : DbContext
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<OutboxDispatcher<TDbContext>>();
        return services;
    }
}
