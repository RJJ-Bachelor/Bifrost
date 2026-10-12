using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Application.Persistence;

namespace Shared.Infrastructure.Persistence.UnitOfWork;

public static class UnitOfWorkServiceCollectionExtensions
{
    public static IServiceCollection AddUnitOfWork<TOwner, TDbContext>(this IServiceCollection services) where TDbContext : DbContext
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IUnitOfWork<TOwner>, UnitOfWork<TOwner, TDbContext>>();
        return services;
    }
}
