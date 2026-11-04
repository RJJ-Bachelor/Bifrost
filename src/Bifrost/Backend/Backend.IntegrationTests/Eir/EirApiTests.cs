using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.IntegrationTests.Infrastructure;
using EirService.Api.Middleware;
using EirService.Api.Middleware.Commands;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Application.Features.Teacher.JoinSession;
using EirService.Participants.Domain.Entities;
using EirService.Participants.Infrastructure;
using EirService.Participants.Infrastructure.Persistence;
using EirService.Sessions.Application.Features.Teacher.Common;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using EirService.Sessions.Application.Features.Teacher.UpdateSession;
using EirService.Sessions.Infrastructure;
using EirService.Sessions.Infrastructure.Persistence;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backend.IntegrationTests.Eir;

// FastEndpoints command/event registries are process-global; keep independent hosts sequential.
[CollectionDefinition("Backend hosts", DisableParallelization = true)]
public sealed class BackendHostsCollection;

[Collection("Backend hosts")]
public sealed class EirApiTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase sessions = new();
    private readonly SqliteTestDatabase participants = new();
    private readonly TestAuthentication authentication = new();
    private WebApplication app = null!;
    private HttpClient client = null!;
    private const string StudentId = "11111111111111111111111111111111";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:eir"] = "Host=unused;Database=unused",
            ["ConnectionStrings:rabbitmq"] = "amqp://unused"
        });
        builder.Services.AddSessionsInfrastructure(builder.Configuration);
        builder.Services.AddParticipantsInfrastructure(builder.Configuration);
        sessions.Register<EirSessionDbContext>(builder.Services);
        participants.Register<EirParticipantDbContext>(builder.Services);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(EirService.Api.Program).Assembly,
                typeof(CreateSessionCommandHandler).Assembly, typeof(JoinSessionCommandHandler).Assembly];
        });
        builder.Services.AddTransient<IValidator<CreateSessionCommand>, CreateSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<UpdateSessionCommand>, UpdateSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<JoinSessionCommand>, JoinSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<JoinTeacherSessionCommand>, JoinTeacherSessionCommandValidator>();
        builder.Services.AddCommandMiddleware(options => options.Register(typeof(CommandValidationMiddleware<,>),
            typeof(CommandLoggingMiddleware<,>)));
        authentication.Register(builder);
        builder.Services.RemoveAll<IHostedService>();
        app = builder.Build();
        app.UseMiddleware<ValidationExceptionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseFastEndpoints(configuration =>
        {
            configuration.Endpoints.RoutePrefix = "api";
            configuration.Serializer.Options.Converters.Add(new JsonStringEnumConverter());
        });
        await app.StartAsync();
        client = app.GetTestClient();
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>().Database.EnsureCreatedAsync();
    }

    [Theory]
    [InlineData("/api/teachers/me")]
    [InlineData("/api/teachers/sessions")]
    [InlineData("/api/students/me")]
    [InlineData("/api/students/sessions")]
    public async Task ProtectedRoutesRejectMissingAuthentication(string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Student", "Bifrost")]
    [InlineData("Teacher", "other")]
    public async Task TeacherPolicyRejectsWrongRoleOrScope(string role, string scope)
    {
        authentication.Teacher(client, "teacher", role, scope);
        using var response = await client.GetAsync("/api/teachers/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SignedTeacherIdentityComesFromValidatedClaims()
    {
        authentication.Teacher(client, "teacher-subject");
        using var response = await client.GetAsync("/api/teachers/me?userId=spoofed");
        response.EnsureSuccessStatusCode();
        var identity = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("teacher-subject", identity.GetProperty("userId").GetString());
        Assert.Equal("Integration Teacher", identity.GetProperty("name").GetString());
    }

    [Fact]
    public async Task StudentIdentityUsesProtectedCookieAndCannotAccessTeacherRoutes()
    {
        TestAuthentication.Student(client, app.Services, StudentId);
        using var identity = await client.GetAsync("/api/students/me?userId=spoofed");
        identity.EnsureSuccessStatusCode();
        Assert.Equal(StudentId, (await identity.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString());
        using var forbidden = await client.GetAsync("/api/teachers/me");
        Assert.Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);
    }

    [Fact]
    public async Task CreateGetAndRenamePersistTheSessionAndNotificationOutbox()
    {
        var created = await CreateSessionAsync();
        Assert.NotEqual(created.Code, created.TeacherCode);
        Assert.Equal(8, created.Code.Length);
        using var renamed = await client.PatchAsJsonAsync($"/api/teachers/sessions/{created.Id}", new { Title = " Revised session " });
        renamed.EnsureSuccessStatusCode();
        Assert.Equal("Revised session", (await renamed.Content.ReadFromJsonAsync<SessionResult>())!.Title);
        using var fetched = await client.GetAsync($"/api/teachers/sessions/{created.Id}");
        fetched.EnsureSuccessStatusCode();
        Assert.Equal("Revised session", (await fetched.Content.ReadFromJsonAsync<SessionResult>())!.Title);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        Assert.Equal("Revised session", (await db.Sessions.SingleAsync()).Title);
        var notification = Assert.Single(await db.Outbox.ToArrayAsync());
        Assert.Null(notification.PublishedAtUtc);
        Assert.Contains("Renamed", notification.Payload);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task InvalidCreateReturns400WithoutWriting(string title)
    {
        authentication.Teacher(client, "owner");
        using var response = await client.PostAsJsonAsync("/api/teachers/sessions", new { Title = title });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Sessions.ToArrayAsync());
    }

    [Fact]
    public async Task NonMemberCannotReadOrRenameAnotherTeachersSession()
    {
        var created = await CreateSessionAsync();
        authentication.Teacher(client, "outsider");
        using var get = await client.GetAsync($"/api/teachers/sessions/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        using var update = await client.PatchAsJsonAsync($"/api/teachers/sessions/{created.Id}", new { Title = "Stolen" });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(created.Title, (await scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Sessions.SingleAsync()).Title);
    }

    [Fact]
    public async Task StudentJoinIsIdempotentAndListsOnlyTheirMembership()
    {
        var created = await CreateSessionAsync();
        client.DefaultRequestHeaders.Authorization = null;
        TestAuthentication.Student(client, app.Services, StudentId);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var joined = await client.PostAsJsonAsync("/api/students/sessions/join", new { created.Code });
            joined.EnsureSuccessStatusCode();
        }
        using var listing = await client.GetAsync("/api/students/sessions");
        listing.EnsureSuccessStatusCode();
        Assert.Equal(created.Id, Assert.Single((await listing.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()).GetProperty("id").GetGuid());
        await using var scope = app.Services.CreateAsyncScope();
        var member = Assert.Single(await scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>().Participants.ToArrayAsync());
        Assert.Equal(StudentId, member.SubjectId);
        Assert.Equal(ParticipantRole.Student, member.Role);
        using var other = app.GetTestClient();
        TestAuthentication.Student(other, app.Services, "22222222222222222222222222222222");
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>("/api/students/sessions")).EnumerateArray());
    }

    [Fact]
    public async Task TeacherJoinAllowsCollaboratorToReadAndRename()
    {
        var created = await CreateSessionAsync();
        authentication.Teacher(client, "collaborator");
        using var joined = await client.PostAsJsonAsync("/api/teachers/sessions/join", new { Code = created.TeacherCode });
        joined.EnsureSuccessStatusCode();
        Assert.Equal(created.Id, (await joined.Content.ReadFromJsonAsync<SessionResult>())!.Id);
        using var update = await client.PatchAsJsonAsync($"/api/teachers/sessions/{created.Id}", new { Title = "Collaborative title" });
        update.EnsureSuccessStatusCode();
        await using var scope = app.Services.CreateAsyncScope();
        var member = Assert.Single(await scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>().Participants.ToArrayAsync());
        Assert.Equal("collaborator", member.SubjectId);
        Assert.Equal(ParticipantRole.Teacher, member.Role);
    }

    [Fact]
    public async Task StudentCannotJoinUsingTheTeacherCode()
    {
        var created = await CreateSessionAsync();
        client.DefaultRequestHeaders.Authorization = null;
        TestAuthentication.Student(client, app.Services, StudentId);
        using var rejected = await client.PostAsJsonAsync("/api/students/sessions/join", new { Code = created.TeacherCode });
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>().Participants.ToArrayAsync());
    }

    private async Task<SessionResult> CreateSessionAsync()
    {
        authentication.Teacher(client, "owner");
        using var response = await client.PostAsJsonAsync("/api/teachers/sessions", new { Title = "Integration session" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionResult>())!;
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (app is not null) await app.DisposeAsync();
        await sessions.DisposeAsync();
        await participants.DisposeAsync();
    }
}
