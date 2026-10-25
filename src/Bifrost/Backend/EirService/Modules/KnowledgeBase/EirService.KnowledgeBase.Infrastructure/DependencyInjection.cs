using EirService.KnowledgeBase.Application.Repositories;
using EirService.KnowledgeBase.Infrastructure.Messaging;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using EirService.KnowledgeBase.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Persistence.Messaging;
using Shared.Infrastructure.Persistence.Outbox;
using Shared.Infrastructure.Persistence.UnitOfWork;

namespace EirService.KnowledgeBase.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKnowledgeBaseInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("eir")
            ?? throw new InvalidOperationException("The 'eir' PostgreSQL connection string is not configured.");
        services.AddDbContext<EirKnowledgeBaseDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "knowledgebase")));
        services.AddScoped<IFaqRepository, FaqRepository>();
        services.AddUnitOfWork<IFaqRepository, EirKnowledgeBaseDbContext>();
        services.AddRabbitMqMessageBus(configuration);
        services.AddOutbox<EirKnowledgeBaseDbContext>(TimeSpan.FromMilliseconds(500));
        services.AddHostedService<RegisterGeneralizationMessageSubscriber>();
        services.AddHostedService<FaqAnsweredMessageSubscriber>();
        return services;
    }
}
