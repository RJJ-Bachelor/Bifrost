using EirService.Contracts.Participants.GetSessionTeachers;
using EirService.Contracts.Participants.RememberSessionTeacher;
using EirService.Participants.Application.Features.Internal.GetSessionTeachers;
using EirService.Participants.Application.Features.Internal.RememberSessionTeacher;
using EirService.Contracts.Participants.GetSessionParticipantIds;
using EirService.Participants.Application.Features.Internal.GetSessionParticipantIds;
using EirService.Participants.Application.Repositories;
using EirService.Contracts.Participants.GetSessionTeacherIds;
using EirService.Participants.Application.Features.Internal.GetSessionTeacherIds;
using FastEndpoints;
using EirService.Participants.Infrastructure.Persistence;
using EirService.Participants.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Shared.Infrastructure.Persistence;
using System.Data.Common;

namespace EirService.Participants.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddParticipantsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("eir")
            ?? throw new InvalidOperationException("The 'eir' PostgreSQL connection string is not configured.");
        services.AddSharedPostgresDataSource("eir", new NpgsqlConnectionStringBuilder(connectionString), NpgsqlDataSource.Create);
        services.AddDbContext<EirParticipantDbContext>((provider, options) => options.UseNpgsql(provider.GetRequiredKeyedService<DbDataSource>("eir"),
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "participants")));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IParticipantRepository, ParticipantRepository>();
        services.AddScoped<IStudentIdentityTransferRepository, StudentIdentityTransferRepository>();
        services.AddScoped<ICommandHandler<GetSessionTeacherIdsQuery, string[]>, GetSessionTeacherIdsQueryHandler>();
        services.AddScoped<ICommandHandler<GetSessionTeachersQuery, IReadOnlyList<SessionTeacher>>, GetSessionTeachersQueryHandler>();
        services.AddScoped<ICommandHandler<RememberSessionTeacherCommand>, RememberSessionTeacherCommandHandler>();
        services.AddScoped<ICommandHandler<GetSessionParticipantIdsQuery, string[]>, GetSessionParticipantIdsQueryHandler>();
        return services;
    }
}
