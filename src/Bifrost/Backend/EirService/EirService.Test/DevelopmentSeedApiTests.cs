using EirService.Contracts.Sessions.GetSessionTeacherNames;
using EirService.Sessions.Application.Features.Internal.GetSessionTeacherNames;
using EirService.Contracts.Participants.GetSessionTeachers;
using EirService.Contracts.Participants.RememberSessionTeacher;
using EirService.Participants.Application.Features.Internal.GetSessionTeachers;
using EirService.Participants.Application.Features.Internal.RememberSessionTeacher;
using System.Net;
using System.Net.Http.Json;
using EirService.Api.Development;
using EirService.Api.Endpoints.Test.Seed;
using EirService.Api.Middleware;
using EirService.Api.Middleware.Commands;
using EirService.Contracts.Participants.GetSessionTeacherIds;
using EirService.Contracts.Participants.GetSessionParticipantIds;
using EirService.Contracts.Sessions.GetHelpRequestSession;
using EirService.HelpRequests.Application;
using EirService.Insights.Application;
using EirService.Insights.Application.Features.Teacher.GetSessionStatistics;
using EirService.HelpRequests.Application.Features.Student.CreateHelpRequest;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.Participants.Application.Features.Internal.GetSessionTeacherIds;
using EirService.Participants.Application.Features.Internal.GetSessionParticipantIds;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using EirService.Sessions.Application.Features.Internal.GetHelpRequestSession;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Api.Authentication;
using Shared.Application.Persistence;

namespace EirService.Test;

[Collection("FastEndpoints")]
public sealed class DevelopmentSeedApiTests
{
    [Fact]
    public async Task SeedsThroughExistingCommandsAndReturnsUsableStudentCookiesAndMimirPayload()
    {
        await using var app = await BuildAsync();
        using var teacher = TeacherClient(app);
        using var response = await teacher.PostAsJsonAsync("/api/test/seed", new { TeacherId = "spoofed" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<SeedDevelopmentDataResult>())!;
        Assert.Equal("faq-demo", result.Scenario);
        Assert.Equal("trusted-teacher", result.Session.CreatedByTeacherId);
        Assert.Equal("Trusted Teacher", result.Session.CreatedByDisplayName);
        Assert.Equal(6, result.Students.Count);
        Assert.Equal(6, result.Students.Select(student => student.StudentId).Distinct().Count());
        Assert.Equal(6, result.Students.Select(student => student.HelpRequestId).Distinct().Count());
        Assert.Contains(result.Students, student => student.HelpType == "Physical");
        Assert.Contains(result.Students, student => student.HelpType == "Digital");
        var requests = app.Services.GetRequiredService<CommandBusTests.RecordingRequestServices>();
        Assert.Equal(6, requests.Saved.Count);
        Assert.Equal(6, requests.RequestMessages.Count);
        var participants = app.Services.GetRequiredService<MemoryParticipantRepository>();
        foreach (var student in result.Students)
        {
            Assert.True(await participants.IsParticipantAsync(result.Session.Id, student.StudentId, ParticipantRole.Student, default));
            var request = Assert.Single(requests.Saved, request => request.Id == student.HelpRequestId);
            Assert.Equal(student.StudentId, request.StudentId);
            Assert.Equal(result.Session.Id, request.SessionId);
            Assert.Equal(HelpRequestStatus.Pending, request.Status);
        }
        Assert.Equal(result.Session.Id, result.Generalization.SessionId);
        Assert.Equal(result.Session.CurrentGeneration, result.Generalization.Generation);
        Assert.Equal(result.Students.Take(2).Select(student => student.HelpRequestId), result.Generalization.HelpRequestIds);
        Assert.All(result.Students.Where(student => result.Generalization.HelpRequestIds.Contains(student.HelpRequestId)),
            student => Assert.Equal("Digital", student.HelpType));
        Assert.NotEqual(Guid.Empty, result.Generalization.GeneralizationId);
        using var studentClient = app.GetTestClient();
        studentClient.DefaultRequestHeaders.Add("Cookie", result.Students[0].CookieHeader);
        using var own = await studentClient.GetAsync($"/api/students/requests/{result.Students[0].HelpRequestId}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using var foreign = await studentClient.GetAsync($"/api/students/requests/{result.Students[1].HelpRequestId}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task EachManualRunCreatesANewSessionAndKeepsThePreviousData()
    {
        await using var app = await BuildAsync();
        using var teacher = TeacherClient(app);
        var results = new List<SeedDevelopmentDataResult>();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await teacher.PostAsJsonAsync("/api/test/seed", new { Scenario = "faq-demo", StudentCount = 2 });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            results.Add((await response.Content.ReadFromJsonAsync<SeedDevelopmentDataResult>())!);
        }
        Assert.NotEqual(results[0].Session.Id, results[1].Session.Id);
        Assert.NotEqual(results[0].Session.Code, results[1].Session.Code);
        Assert.Equal(4, app.Services.GetRequiredService<CommandBusTests.RecordingRequestServices>().Saved.Count);
        Assert.Equal(2, (await app.Services.GetRequiredService<MemorySessionRepository>().GetByTeacherAsync("trusted-teacher", default)).Count);
    }

    [Theory]
    [InlineData("faq-demo", 1)]
    [InlineData("faq-demo", 21)]
    [InlineData("unknown", 6)]
    public async Task InvalidInputCreatesNoData(string scenario, int studentCount)
    {
        await using var app = await BuildAsync();
        using var teacher = TeacherClient(app);
        using var response = await teacher.PostAsJsonAsync("/api/test/seed", new { Scenario = scenario, StudentCount = studentCount });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(app.Services.GetRequiredService<CommandBusTests.RecordingRequestServices>().Saved);
        Assert.Empty(await app.Services.GetRequiredService<MemorySessionRepository>().GetByTeacherAsync("trusted-teacher", default));
    }

    [Theory]
    [InlineData(null, "Bifrost", HttpStatusCode.Unauthorized)]
    [InlineData("Student", "Bifrost", HttpStatusCode.Forbidden)]
    [InlineData("Teacher", "other-scope", HttpStatusCode.Forbidden)]
    public async Task RequiresAuthenticatedTeacherWithTheExistingScope(string? role, string scope, HttpStatusCode expected)
    {
        await using var app = await BuildAsync();
        using var client = app.GetTestClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Subject", "caller");
            client.DefaultRequestHeaders.Add("X-Test-Scope", scope);
        }
        using var response = await client.PostAsJsonAsync("/api/test/seed", new { });
        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(app.Services.GetRequiredService<CommandBusTests.RecordingRequestServices>().Saved);
    }

    [Fact]
    public async Task ProductionDoesNotExposeTheEndpointOrRunSeeding()
    {
        await using var app = await BuildAsync(Environments.Production);
        using var teacher = TeacherClient(app);
        using var response = await teacher.PostAsJsonAsync("/api/test/seed", new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(app.Services.GetRequiredService<CommandBusTests.RecordingRequestServices>().Saved);
    }

    private static HttpClient TeacherClient(WebApplication app)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Teacher");
        client.DefaultRequestHeaders.Add("X-Test-Subject", "trusted-teacher");
        client.DefaultRequestHeaders.Add("X-Test-Name", "Trusted Teacher");
        client.DefaultRequestHeaders.Add("X-Test-Scope", "Bifrost");
        return client;
    }

    private static async Task<WebApplication> BuildAsync(string environment = "Development")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Logging.ClearProviders();
        builder.Host.UseDefaultServiceProvider(options => options.ValidateScopes = true);
        builder.WebHost.UseTestServer();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(Api.Program).Assembly, typeof(CreateSessionCommandHandler).Assembly,
                typeof(CreateHelpRequestCommandHandler).Assembly, typeof(JoinSessionCommandHandler).Assembly,
                typeof(GetSessionStatisticsQueryHandler).Assembly];
        });
        builder.Services.AddHelpRequestsApplication();
        builder.Services.AddInsightsApplication();
        builder.Services.AddTransient<IValidator<CreateSessionCommand>, CreateSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<JoinSessionCommand>, JoinSessionCommandValidator>();
        builder.Services.AddCommandMiddleware(options => options.Register(typeof(CommandValidationMiddleware<,>), typeof(CommandLoggingMiddleware<,>)));
        var requests = new CommandBusTests.RecordingRequestServices([]);
        builder.Services.AddSingleton(requests);
        builder.Services.AddScoped<IHelpRequestRepository>(_ => requests);
        builder.Services.AddScoped<IUnitOfWork<IHelpRequestRepository>>(_ => requests);
        builder.Services.AddSingleton<MemorySessionRepository>();
        builder.Services.AddScoped<ISessionRepository>(provider => provider.GetRequiredService<MemorySessionRepository>());
        builder.Services.AddScoped<IUnitOfWork<ISessionRepository>>(provider => provider.GetRequiredService<MemorySessionRepository>());
        builder.Services.AddSingleton<MemoryParticipantRepository>();
        builder.Services.AddScoped<IParticipantRepository>(provider => provider.GetRequiredService<MemoryParticipantRepository>());
        builder.Services.AddScoped<ICommandHandler<GetSessionTeacherIdsQuery, string[]>, GetSessionTeacherIdsQueryHandler>();
        builder.Services.AddScoped<ICommandHandler<GetSessionTeachersQuery, IReadOnlyList<SessionTeacher>>, GetSessionTeachersQueryHandler>();
        builder.Services.AddScoped<ICommandHandler<RememberSessionTeacherCommand>, RememberSessionTeacherCommandHandler>();
        builder.Services.AddScoped<ICommandHandler<GetSessionParticipantIdsQuery, string[]>, GetSessionParticipantIdsQueryHandler>();
        builder.Services.AddScoped<ICommandHandler<GetHelpRequestSessionQuery, HelpRequestSession?>, GetHelpRequestSessionQueryHandler>();
        builder.Services.AddScoped<ICommandHandler<GetSessionTeacherNamesQuery, IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string?>>>, GetSessionTeacherNamesQueryHandler>();
        builder.Services.AddSingleton(TimeProvider.System);
        if (builder.Environment.IsDevelopment()) builder.Services.AddScoped<DevelopmentDataSeeder>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, CommandBusTests.TestAuthenticationHandler>(JwtBearerDefaults.AuthenticationScheme, _ => { })
            .AddStudentCookie(builder.Configuration, options => options.DataProtectionProvider = new EphemeralDataProtectionProvider());
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Teacher", policy => policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser().RequireRole("Teacher").RequireClaim("sub")
                .RequireAssertion(context => context.User.FindAll("scope").Any(claim => claim.Value.Split(' ').Contains("Bifrost"))));
            options.AddPolicy("Student", policy => policy.AddAuthenticationSchemes(StudentCookieHandler.SchemeName)
                .RequireAuthenticatedUser().RequireRole("Student").RequireClaim("sub"));
        });
        var app = builder.Build();
        app.UseMiddleware<ValidationExceptionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapDevelopmentSeedApi();
        app.UseFastEndpoints(configuration =>
        {
            configuration.Endpoints.RoutePrefix = "api";
            configuration.Serializer.Options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });
        await app.StartAsync();
        return app;
    }
}
