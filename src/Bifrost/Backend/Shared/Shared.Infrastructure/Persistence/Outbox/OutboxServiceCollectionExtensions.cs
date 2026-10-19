using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Shared.Infrastructure.Persistence.Outbox;

public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddOutbox<TDbContext>(this IServiceCollection services, TimeSpan? pollingInterval = null) where TDbContext : DbContext
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService(provider => ActivatorUtilities.CreateInstance<OutboxDispatcher<TDbContext>>(
            provider, pollingInterval ?? TimeSpan.FromSeconds(5)));
        return services;
    }
}
