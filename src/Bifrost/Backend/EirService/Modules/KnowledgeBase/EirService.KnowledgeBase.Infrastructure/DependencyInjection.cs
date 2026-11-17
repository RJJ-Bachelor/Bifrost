using EirService.Contracts.KnowledgeBase.RecordFaqAnswer;
using EirService.Contracts.Messaging;
using EirService.KnowledgeBase.Application.Repositories;
using EirService.KnowledgeBase.Infrastructure.Messaging;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using EirService.KnowledgeBase.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Persistence;
using Shared.Infrastructure.Persistence.Messaging;
using Shared.Infrastructure.Persistence.Outbox;
using Shared.Application.Messaging.Contracts;
using EirService.KnowledgeBase.Application.Features.Internal.RecordFaqAnswer;
using EirService.KnowledgeBase.Application.Features.Internal.RegisterGeneralization;
using Shared.Infrastructure.Persistence.UnitOfWork;
using Npgsql;
using Shared.Infrastructure.Persistence;
using System.Data.Common;

namespace EirService.KnowledgeBase.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKnowledgeBaseInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("eir")
            ?? throw new InvalidOperationException("The 'eir' PostgreSQL connection string is not configured.");
        services.AddSharedPostgresDataSource("eir", new NpgsqlConnectionStringBuilder(connectionString), NpgsqlDataSource.Create);
        services.AddDbContext<EirKnowledgeBaseDbContext>((provider, options) => options.UseNpgsql(provider.GetRequiredKeyedService<DbDataSource>("eir"),
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "knowledgebase")));
        services.AddScoped<IFaqRepository, FaqRepository>();
        services.AddUnitOfWork<IFaqRepository, EirKnowledgeBaseDbContext>();
        services.AddScoped<IUnitOfWork<IFaqRepository>, KnowledgeBaseUnitOfWork>();
        services.AddRabbitMqMessageBus(configuration);
        services.AddOutbox<EirKnowledgeBaseDbContext>(TimeSpan.FromMilliseconds(500));
        services.AddHostedService<RegisterGeneralizationMessageSubscriber>();
        services.AddModuleMessageRoute<FaqAnsweredIntegrationEvent>(EirModuleRoutes.FaqAnswerRecorded,
            "eir-knowledgebase-faq-answered", "eir.modules", "faq.answered", configuration);
        var maximumAttempts = configuration.GetValue("Messaging:Delivery:MaximumAttempts", 8);
        services.AddMessageReceiver<FaqAnsweredIntegrationEvent, EirKnowledgeBaseDbContext, FaqAnsweredMessageHandler>(
            "eir.knowledgebase.faq-answered", maximumAttempts: maximumAttempts);
        // This existing workflow commits KnowledgeBase before HelpRequests. Completion is recorded only after both steps.
        services.AddMessageReceiver<GeneralizedCreatedMessage, EirKnowledgeBaseDbContext, RegisterGeneralizationMessageHandler>(
            "eir.knowledgebase.generalized-created", transactional: false, maximumAttempts: maximumAttempts);
        services.AddModuleMessageSubscriber<FaqAnsweredIntegrationEvent>(EirModuleRoutes.FaqAnswerRecorded);
        services.AddLegacyModuleCommand<RecordFaqAnswerCommand, FaqAnsweredIntegrationEvent>(
            EirModuleRoutes.FaqAnswered, command => new(command.FaqId, command.SessionId, command.Generation,
                command.Answer, command.TeacherId, command.AnsweredAt, command.Question));
        return services;
    }
}
