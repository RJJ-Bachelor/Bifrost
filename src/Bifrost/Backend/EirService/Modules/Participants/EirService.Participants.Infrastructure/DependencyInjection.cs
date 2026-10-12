using EirService.Participants.Application.Repositories;
using EirService.Participants.Infrastructure.Persistence;
using EirService.Participants.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EirService.Participants.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddParticipantsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("eir")
            ?? throw new InvalidOperationException("The 'eir' PostgreSQL connection string is not configured.");
        services.AddDbContext<EirParticipantDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "participants")));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IParticipantRepository, ParticipantRepository>();
        return services;
    }
}
