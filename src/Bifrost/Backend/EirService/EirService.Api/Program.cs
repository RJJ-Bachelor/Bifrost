using Shared.Api.Authentication;
using EirService.Api.Development;
using EirService.Api.Endpoints.Test.Seed;
using EirService.KnowledgeBase.Application;
using EirService.KnowledgeBase.Infrastructure;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using EirService.KnowledgeBase.Application.Features.Common.GetSessionFaqs;
using System.Text.Json.Serialization;
using EirService.HelpRequests.Application;
using EirService.Insights.Application;
using EirService.Insights.Application.Features.Teacher.GetSessionStatistics;
using EirService.Api.Middleware;
using EirService.Api.Middleware.Commands;
using EirService.HelpRequests.Application.Features.Student.CreateHelpRequest;
using EirService.HelpRequests.Application.Features.Student.GetHelpRequests;
using EirService.HelpRequests.Infrastructure;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using EirService.Sessions.Application.Features.Teacher.ClearSession;
using EirService.Sessions.Application.Features.Teacher.ArchiveSession;
using EirService.Sessions.Application.Features.Common.GetSessionArchives;
using EirService.Sessions.Application.Features.Common.GetSessionArchive;
using EirService.Sessions.Application.Features.Teacher.DeleteSession;
using EirService.Sessions.Application.Features.Teacher.GetSession;
using EirService.Sessions.Application.Features.Teacher.GetSessions;
using EirService.Sessions.Application.Features.Student.GetStudentSession;
using EirService.Sessions.Infrastructure;
using EirService.Sessions.Infrastructure.Persistence;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Application.Features.Teacher.JoinSession;
using EirService.Participants.Infrastructure;
using EirService.Participants.Infrastructure.Persistence;
using FastEndpoints;
using FluentValidation;
using Shared.Infrastructure.Persistence.Migrations;
using Microsoft.AspNetCore.HttpOverrides;

namespace EirService.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddHelpRequestsInfrastructure(builder.Configuration);
        builder.Services.AddHelpRequestsApplication();
        builder.Services.AddInsightsApplication();
        builder.Services.AddSessionsInfrastructure(builder.Configuration);
        builder.Services.AddParticipantsInfrastructure(builder.Configuration);
        builder.Services.AddKnowledgeBaseInfrastructure(builder.Configuration);
        builder.Services.AddKnowledgeBaseApplication();
        if (builder.Environment.IsDevelopment()) builder.Services.AddScoped<DevelopmentDataSeeder>();

        // Add services to the container.
        builder.Services.AddBifrostServiceAuthentication(builder.Configuration, builder.Environment);
        builder.Services.AddFastEndpoints(options =>
        {
            options.Assemblies =
            [
                typeof(CreateSessionCommandHandler).Assembly,
                typeof(CreateHelpRequestCommandHandler).Assembly,
                typeof(JoinSessionCommandHandler).Assembly,
                typeof(GetSessionFaqsQueryHandler).Assembly,
                typeof(GetSessionStatisticsQueryHandler).Assembly
            ];
        });
        builder.Services.AddTransient<IValidator<CreateSessionCommand>, CreateSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<ClearSessionCommand>, ClearSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<ArchiveSessionCommand>, ArchiveSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<GetSessionArchivesQuery>, GetSessionArchivesQueryValidator>();
        builder.Services.AddTransient<IValidator<GetSessionArchiveQuery>, GetSessionArchiveQueryValidator>();
        builder.Services.AddTransient<IValidator<DeleteSessionCommand>, DeleteSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<GetSessionsQuery>, GetSessionsQueryValidator>();
        builder.Services.AddTransient<IValidator<GetSessionQuery>, GetSessionQueryValidator>();
        builder.Services.AddTransient<IValidator<GetStudentSessionQuery>, GetStudentSessionQueryValidator>();
        builder.Services.AddTransient<IValidator<JoinSessionCommand>, JoinSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<JoinTeacherSessionCommand>, JoinTeacherSessionCommandValidator>();
        builder.Services.AddCommandMiddleware(options => options.Register(
            typeof(CommandValidationMiddleware<,>),
            typeof(CommandLoggingMiddleware<,>)));

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        var app = builder.Build();
        // Accept the protocol forwarded by the local gateway.
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedProto
        });

        using (var scope = app.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Database.MigrateAtStartup();
            scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Database.MigrateAtStartup();
            scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>().Database.MigrateAtStartup();
            scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Database.MigrateAtStartup();
        }

        app.MapDefaultEndpoints();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapDevelopmentSeedApi();
        }

        app.UseMiddleware<ValidationExceptionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseFastEndpoints(configuration =>
        {
            configuration.Endpoints.RoutePrefix = "api";
            configuration.Serializer.Options.Converters.Add(new JsonStringEnumConverter());
        });

        app.Run();
    }
}
