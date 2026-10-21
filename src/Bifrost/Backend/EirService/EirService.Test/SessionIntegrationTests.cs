using EirService.HelpRequests.Application;
using EirService.HelpRequests.Application.Repositories;
using EirService.Sessions.Domain.DomainEvents;
using EirService.Participants.Domain.ValueObjects;
using Shared.Application.Persistence;
using EirService.Sessions.Application.Repositories;
using Shared.Api.Authentication;
using EirService.Contracts.Sessions.Common;
using EirService.Sessions.Application.Features.Teacher.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.Api.Authentication;
using EirService.Api.Middleware;
using EirService.Api.Middleware.Commands;
using EirService.HelpRequests.Application.Features.Student.CreateHelpRequest;
using EirService.HelpRequests.Application.Features.Student.GetHelpRequests;
using EirService.HelpRequests.Application.Features.Internal.GeneralizedMessage;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Infrastructure;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Application.Features.Teacher.JoinSession;
using EirService.Participants.Infrastructure;
using EirService.Participants.Infrastructure.Persistence;
using EirService.Sessions.Application.Features.Teacher.ClearSession;
using EirService.Sessions.Application.Features.Teacher.DeleteSession;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using EirService.Sessions.Application.Features.Teacher.GetSession;
using EirService.Sessions.Application.Features.Teacher.GetSessions;
using EirService.Sessions.Application.Features.Student.GetStudentSession;
using EirService.Sessions.Domain.Entities;
using EirService.Sessions.Domain.ValueObjects;
using EirService.Sessions.Infrastructure;
using EirService.Sessions.Infrastructure.Persistence;
using EirService.Sessions.Infrastructure.Repositories;
using EirService.KnowledgeBase.Infrastructure;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using EirService.KnowledgeBase.Application.Features.GetSessionFaqs;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Outbox;
using Shared.Infrastructure.Persistence.Migrations;
using EirService.HelpRequests.Infrastructure.Repositories;

namespace EirService.Test;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIFROST_TEST_POSTGRES")))
            Skip = "Set BIFROST_TEST_POSTGRES to an isolated PostgreSQL server with CREATE DATABASE permission.";
    }
}

[Collection("FastEndpoints")]
public sealed partial class SessionIntegrationTests : IAsyncLifetime
{
    private string? adminConnectionString;
    private string databaseName = null!;
    private string connectionString = null!;
    private WebApplication app = null!;
    private HttpClient teacher = null!;
    private HttpClient student = null!;
    private readonly TestClock clock = new();
    private readonly RecordingBus bus = new();
    private readonly Dictionary<Guid, Guid> createdRequestIds = [];

    public async Task InitializeAsync()
    {
        adminConnectionString = Environment.GetEnvironmentVariable("BIFROST_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(adminConnectionString)) return;
        databaseName = "bifrost_session_test_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin);
            await create.ExecuteNonQueryAsync();
        }
        connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }.ConnectionString;
        // Every test also exercises adopting a database created by the old EnsureCreated setup.
        await using (var legacy = new NpgsqlConnection(connectionString))
        {
            await legacy.OpenAsync();
            await using var seed = new NpgsqlCommand("""
                CREATE TABLE requests ("Id" varchar(100) PRIMARY KEY, "Message" varchar(4000) NOT NULL);
                INSERT INTO requests ("Id", "Message") VALUES ('legacy-row', 'Preserve this legacy request');
                """, legacy);
            await seed.ExecuteNonQueryAsync();
        }

        var builder = WebApplication.CreateBuilder();
        builder.Host.UseDefaultServiceProvider(options => options.ValidateScopes = true);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:eir"] = connectionString,
            ["ConnectionStrings:rabbitmq"] = "amqp://unused-in-tests"
        });
        builder.Services.AddHelpRequestsInfrastructure(builder.Configuration);
        builder.Services.AddHelpRequestsApplication();
        builder.Services.AddSessionsInfrastructure(builder.Configuration);
        builder.Services.AddParticipantsInfrastructure(builder.Configuration);
        builder.Services.AddKnowledgeBaseInfrastructure(builder.Configuration);
        builder.Services.RemoveAll<IHostedService>();
        builder.Services.RemoveAll<IMessageBus>();
        builder.Services.AddSingleton<IMessageBus>(bus);
        builder.Services.RemoveAll<TimeProvider>();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(Api.Program).Assembly, typeof(CreateSessionCommandHandler).Assembly,
                typeof(CreateHelpRequestCommandHandler).Assembly, typeof(JoinSessionCommandHandler).Assembly, typeof(GetSessionFaqsQueryHandler).Assembly];
        });
        builder.Services.AddTransient<IValidator<CreateSessionCommand>, CreateSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<ClearSessionCommand>, ClearSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<DeleteSessionCommand>, DeleteSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<GetSessionQuery>, GetSessionQueryValidator>();
        builder.Services.AddTransient<IValidator<GetSessionsQuery>, GetSessionsQueryValidator>();
        builder.Services.AddTransient<IValidator<GetStudentSessionQuery>, GetStudentSessionQueryValidator>();
        builder.Services.AddTransient<IValidator<JoinSessionCommand>, JoinSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<JoinTeacherSessionCommand>, JoinTeacherSessionCommandValidator>();
        builder.Services.AddCommandMiddleware(options => options.Register(typeof(CommandValidationMiddleware<,>), typeof(CommandLoggingMiddleware<,>)));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, CommandBusTests.TestAuthenticationHandler>(JwtBearerDefaults.AuthenticationScheme, _ => { })
            .AddStudentCookie(builder.Configuration, options => options.DataProtectionProvider = new EphemeralDataProtectionProvider());
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Student", policy => policy.AddAuthenticationSchemes(StudentCookieHandler.SchemeName)
                .RequireAuthenticatedUser().RequireClaim("sub").RequireRole("Student"));
            options.AddPolicy("Teacher", policy => policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser().RequireClaim("sub").RequireRole("Teacher")
                .RequireAssertion(context => context.User.FindAll("scope").Any(claim => claim.Value.Split(' ').Contains("Bifrost"))));
        });
        app = builder.Build();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Database.MigrateAtStartupAsync();
            await scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Database.MigrateAtStartupAsync();
            await scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>().Database.MigrateAtStartupAsync();
            await scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Database.MigrateAtStartupAsync();
        }
        app.UseMiddleware<ValidationExceptionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseFastEndpoints(configuration =>
        {
            configuration.Endpoints.RoutePrefix = "api";
            configuration.Serializer.Options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });
        await app.StartAsync();
        teacher = app.GetTestClient();
        teacher.DefaultRequestHeaders.Add("X-Test-Role", "Teacher");
        teacher.DefaultRequestHeaders.Add("X-Test-Scope", "Bifrost");
        teacher.DefaultRequestHeaders.Add("X-Test-Subject", "provider|opaque-teacher");
        student = app.GetTestClient();
        AuthenticateStudent(student);
    }

    private void AuthenticateStudent(HttpClient client)
    {
        var cookie = app.Services.GetRequiredService<StudentIdentityCookie>().Protect(Guid.NewGuid().ToString("N"));
        client.DefaultRequestHeaders.Add("Cookie", $"{StudentCookieHandler.CookieName}={cookie}");
    }

    [PostgresFact]
    public async Task CreatePersistsTrustedMetadataAndServerTime()
    {
        teacher.DefaultRequestHeaders.Add("X-Test-Name", "Trusted Teacher");
        using var response = await teacher.PostAsJsonAsync("/api/teachers/sessions", new
        {
            Title = "  Algorithms  ", CreatedByTeacherId = "spoofed", CreatedByDisplayName = "Spoofed",
            CreatedAtUtc = DateTimeOffset.MinValue, Code = "ZZZZZZZZ", TeacherCode = "ZZZZZZZZ", CurrentGeneration = 88
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<SessionResult>())!;
        Assert.Equal("Algorithms", result.Title);
        Assert.Equal("provider|opaque-teacher", result.CreatedByTeacherId);
        Assert.Equal("Trusted Teacher", result.CreatedByDisplayName);
        Assert.Equal(clock.Now, result.CreatedAtUtc);
        Assert.Equal(1, result.CurrentGeneration);
        Assert.Null(result.LastClearedAtUtc);
        Assert.True(SessionCode.IsValid(result.Code));
        Assert.True(SessionCode.IsValid(result.TeacherCode));
        Assert.NotEqual(result.Code, result.TeacherCode);
        Assert.Equal($"/api/teachers/sessions/{result.Id}", response.Headers.Location!.ToString());
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        var saved = await db.Sessions.SingleAsync();
        Assert.Equal(result, SessionResult.From(saved));

    }

    [PostgresFact]
    public async Task MissingNameIsNullAndTitlesAreValidatedAfterTrimming()
    {
        foreach (var title in new[] { "", "   ", new string('x', 201) })
        {
            using var invalid = await teacher.PostAsJsonAsync("/api/teachers/sessions", new { Title = title });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var missing = await teacher.PostAsJsonAsync("/api/teachers/sessions", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var created = await CreateAsync(" " + new string('x', 200) + " ");
        Assert.Null(created.CreatedByDisplayName);
        Assert.Equal(200, created.Title.Length);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Sessions.CountAsync());
    }

    [PostgresFact]
    public async Task TeacherListAndDetailExcludeOtherTeachersSessions()
    {
        var mine = await CreateAsync();
        teacher.DefaultRequestHeaders.Remove("X-Test-Subject");
        teacher.DefaultRequestHeaders.Add("X-Test-Subject", "other-teacher");
        var other = await CreateAsync("Other session");
        var list = (await teacher.GetFromJsonAsync<SessionResult[]>("/api/teachers/sessions?teacherId=provider%7Copaque-teacher"))!;
        Assert.Equal(other.Id, Assert.Single(list).Id);
        using var denied = await teacher.GetAsync($"/api/teachers/sessions/{mine.Id}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var clearDenied = await teacher.PostAsJsonAsync($"/api/teachers/sessions/{mine.Id}/clear", new { ExpectedGeneration = 1 });
        Assert.Equal(HttpStatusCode.NotFound, clearDenied.StatusCode);
        Assert.Equal(other, await teacher.GetFromJsonAsync<SessionResult>($"/api/teachers/sessions/{other.Id}"));
    }

    [PostgresFact]
    public async Task StudentRequiresMembershipAndDtoDoesNotExposeCodeOrCreator()
    {
        var session = await CreateAsync();
        using var denied = await student.GetAsync($"/api/students/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var deniedRequest = await student.PostAsJsonAsync($"/api/students/sessions/{session.Id}/requests", new { Message = "Help", HelpType = HelpType.Digital });
        Assert.Equal(HttpStatusCode.NotFound, deniedRequest.StatusCode);
        await JoinAsync(session.Code.ToLowerInvariant());
        await JoinAsync(session.Code);
        using var response = await student.GetAsync($"/api/students/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("code", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createdBy", body, StringComparison.OrdinalIgnoreCase);
        var list = (await student.GetFromJsonAsync<SessionSummary[]>("/api/students/sessions"))!;
        Assert.Equal(session.Id, Assert.Single(list).Id);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>().Participants.CountAsync());
        using var stranger = app.GetTestClient();
        AuthenticateStudent(stranger);
        using var strangerResponse = await stranger.GetAsync($"/api/students/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.NotFound, strangerResponse.StatusCode);
    }

    [PostgresFact]
    public async Task ClearHidesOldRequestsPreservesMembershipAndAcceptsNewGeneration()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        await CreateRequestAsync(session.Id, 1, TestIds.From("old-request"));
        clock.Now = clock.Now.AddHours(1);
        using var response = await ClearAsync(session.Id, 1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cleared = (await response.Content.ReadFromJsonAsync<ClearSessionResult>())!;
        Assert.Equal(new ClearSessionResult(session.Id, 2, clock.Now), cleared);
        Assert.Empty((await student.GetFromJsonAsync<HelpRequestResult[]>($"/api/students/requests?sessionId={session.Id}"))!);
        Assert.Empty((await student.GetFromJsonAsync<HelpRequestResult[]>("/api/students/requests"))!);
        var after = (await teacher.GetFromJsonAsync<SessionResult>($"/api/teachers/sessions/{session.Id}"))!;
        Assert.Equal(session with { CurrentGeneration = 2, LastClearedAtUtc = clock.Now }, after);
        var studentSession = (await student.GetFromJsonAsync<SessionSummary>($"/api/students/sessions/{session.Id}"))!;
        Assert.Equal(2, studentSession.CurrentGeneration);
        using var stale = await student.PostAsJsonAsync($"/api/students/requests/{createdRequestIds[TestIds.From("old-request")]}/messages",
            new { Text = "Late follow-up" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await CreateRequestAsync(session.Id, 2, TestIds.From("new-request"));
        var current = (await student.GetFromJsonAsync<HelpRequestResult[]>("/api/students/requests"))!;
        Assert.Equal(createdRequestIds[TestIds.From("new-request")], Assert.Single(current).Id);
        Assert.Equal(2, current[0].Generation);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var queued = await db.Outbox.SingleAsync(message => message.MessageId == createdRequestIds[TestIds.From("new-request")] && message.RoutingKey == MessageRoutes.HelpRequestCreated);
        var payload = JsonSerializer.Deserialize<HelpRequestCreatedMessage>(queued.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(createdRequestIds[TestIds.From("new-request")], payload.HelpRequestId);
        Assert.Equal(session.Id, payload.SessionId);
        Assert.Equal(2, payload.Generation);
        Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Requests.CountAsync());
    }

    [PostgresFact]
    public async Task DelayedOldWriteRemainsInvisibleAfterClear()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var identity = (await student.GetFromJsonAsync<EirService.Api.Endpoints.Student.Participants.GetStudentIdentity.StudentIdentityResponse>("/api/students/me"))!;
        // Represents an operation that captured generation 1 before the clear and commits afterwards.
        var delayed = TestHelpRequests.Create(TestIds.From("delayed"), identity.UserId, "Old content", session.Id, 1);
        using var cleared = await ClearAsync(session.Id, 1);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            db.Requests.Add(delayed);
            await db.SaveChangesAsync();
        }
        Assert.Empty((await student.GetFromJsonAsync<HelpRequestResult[]>("/api/students/requests"))!);
    }

    [PostgresFact]
    public async Task SimultaneousAndRepeatedClearsIncrementExactlyOnce()
    {
        var session = await CreateAsync();
        var responses = await Task.WhenAll(ClearAsync(session.Id, 1), ClearAsync(session.Id, 1));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        }
        finally { foreach (var response in responses) response.Dispose(); }
        using var retry = await ClearAsync(session.Id, 1);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        Assert.Equal(2, (await db.Sessions.SingleAsync()).CurrentGeneration);
        Assert.Equal(1, await db.Outbox.CountAsync());
    }

    [PostgresFact]
    public async Task DatabaseConcurrencyConflictRejectsSecondClear()
    {
        var session = await CreateAsync();
        await using var firstScope = app.Services.CreateAsyncScope();
        await using var secondScope = app.Services.CreateAsyncScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        var first = await firstDb.Sessions.SingleAsync();
        var second = await secondDb.Sessions.SingleAsync();
        first.Clear(1, clock.Now);
        second.Clear(1, clock.Now);
        await firstScope.ServiceProvider.GetRequiredService<IUnitOfWork<ISessionRepository>>().SaveChangesAsync();
        await Assert.ThrowsAsync<UnitOfWorkConcurrencyException>(() =>
            secondScope.ServiceProvider.GetRequiredService<IUnitOfWork<ISessionRepository>>().SaveChangesAsync());
        Assert.Equal(1, await firstDb.Outbox.CountAsync());
        Assert.Empty(first.GetDomainEvents());
        Assert.Single(second.GetDomainEvents());
        Assert.Equal(2, (await firstDb.Sessions.SingleAsync()).CurrentGeneration);
    }

    [PostgresFact]
    public async Task ClearPromotesDomainEventAndCommitsIntegrationEventOnce()
    {
        var created = await CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        var session = await db.Sessions.SingleAsync();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<ISessionRepository>>();
        clock.Now = clock.Now.AddMinutes(1);
        session.Clear(1, clock.Now);
        var domainEvent = Assert.IsType<SessionClearedDomainEvent>(Assert.Single(session.GetDomainEvents()));
        await unitOfWork.SaveChangesAsync();
        Assert.Empty(session.GetDomainEvents());
        var queued = await db.Outbox.SingleAsync();
        var integrationEvent = JsonSerializer.Deserialize<SessionUpdatedIntegrationEvent>(queued.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(domainEvent.Id, integrationEvent.EventId);
        Assert.Equal(domainEvent.Id, queued.MessageId);
        Assert.Equal(created.Id, integrationEvent.SessionId);
        Assert.Equal(1, integrationEvent.PreviousGeneration);
        Assert.Equal(2, integrationEvent.Generation);
        Assert.Equal(SessionUpdateType.Cleared, integrationEvent.EventType);
        Assert.Equal(clock.Now, integrationEvent.UpdatedAtUtc);
        Assert.Equal(MessageRoutes.Sessions, queued.ExchangeName);
        Assert.Equal(MessageRoutes.SessionUpdated, queued.RoutingKey);
        Assert.Null(queued.PublishedAtUtc);
        Assert.Empty(bus.Messages);
        await unitOfWork.SaveChangesAsync();
        Assert.Equal(1, await db.Outbox.CountAsync());
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirSessionDbContext>>(app.Services);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Equal(MessageRoutes.SessionUpdated, Assert.Single(bus.Messages).RoutingKey);
    }

    [PostgresFact]
    public async Task FailedIntegrationEventInsertRollsBackClearAndCanRetryPendingDomainEvent()
    {
        await CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE sessions.outbox ADD CONSTRAINT "RejectClearForTest" CHECK ("RoutingKey" <> 'session.updated');
            """);
        var session = await db.Sessions.SingleAsync();
        session.Clear(1, clock.Now);
        var domainEvent = Assert.Single(session.GetDomainEvents());
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<ISessionRepository>>();
        await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.SaveChangesAsync());
        Assert.Single(session.GetDomainEvents());
        await using (var verification = app.Services.CreateAsyncScope())
        {
            var saved = verification.ServiceProvider.GetRequiredService<EirSessionDbContext>();
            Assert.Equal(1, (await saved.Sessions.SingleAsync()).CurrentGeneration);
            Assert.Empty(await saved.Outbox.ToArrayAsync());
        }
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE sessions.outbox DROP CONSTRAINT "RejectClearForTest";
            """);
        await unitOfWork.SaveChangesAsync();
        Assert.Empty(session.GetDomainEvents());
        Assert.Equal(domainEvent.Id, (await db.Outbox.SingleAsync()).MessageId);
        Assert.Equal(2, (await db.Sessions.SingleAsync()).CurrentGeneration);
    }

    [PostgresFact]
    public async Task ParticipantIdentityUsesExistingCompositeKeyAfterMaterialization()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        await using var scope = app.Services.CreateAsyncScope();
        var participant = await scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>().Participants.SingleAsync();
        Assert.Equal(new SessionParticipantId(participant.SessionId, participant.SubjectId, participant.Role), participant.Id);
    }

    [PostgresFact]
    public async Task FailedOutboxInsertRollsBackHelpRequest()
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            var request = TestHelpRequests.Create(TestIds.From("rollback-request"), "student", "Help");
            OutgoingMessage[] messages = [new(request.Id, new HelpRequestCreatedMessage(request.Id, request.SessionId, request.Generation, request.Messages.First().Text.Value), "requests", new string('x', 256))];
            await Assert.ThrowsAsync<DbUpdateException>(() => TestHelpRequests.SaveWithOutboxAsync(db, request, messages));
        }
        await using var verification = app.Services.CreateAsyncScope();
        var saved = verification.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        Assert.False(await saved.Requests.AnyAsync(request => request.Id == TestIds.From("rollback-request")));
        Assert.False(await saved.Set<Dictionary<string, object>>("HelpRequestNumberCounter")
            .AnyAsync(counter => EF.Property<Guid>(counter, "SessionId") == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
        Assert.Empty(await saved.Outbox.ToArrayAsync());
    }

    [PostgresFact]
    public async Task OutboxRetriesFailureAndPublishesBothContracts()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        await CreateRequestAsync(session.Id, 1, TestIds.From("queued-request"));
        Assert.Empty(bus.Messages);
        var dispatcher = new OutboxDispatcher<EirRequestDbContext>(app.Services.GetRequiredService<IServiceScopeFactory>(), bus, clock,
            NullLogger<OutboxDispatcher<EirRequestDbContext>>.Instance);
        bus.Fail = true;
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        await using (var scope = app.Services.CreateAsyncScope())
            Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.CountAsync(message => message.PublishedAtUtc == null));
        bus.Fail = false;
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Contains(bus.Messages, message => message.RoutingKey == MessageRoutes.HelpRequestCreated);
        Assert.Contains(bus.Messages, message => message.RoutingKey == MessageRoutes.NotificationCreated);
        var help = (JsonElement)bus.Messages.Single(message => message.RoutingKey == MessageRoutes.HelpRequestCreated).Payload;
        Assert.Equal(session.Id, help.GetProperty("sessionId").GetGuid());
        Assert.Equal(1, help.GetProperty("generation").GetInt32());
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Equal(3, bus.Messages.Count);
    }

    [PostgresFact]
    public async Task CodeCollisionDoesNotPersistSecondSession()
    {
        var original = await CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        var collision = Session.Create("Collision", new SessionCode(original.Code), SessionCode.Generate(), "teacher", null, clock.Now);
        var repository = new SessionRepository(db);
        Assert.False(await repository.TryAddAsync(collision, CancellationToken.None));
        Assert.Equal(1, await db.Sessions.CountAsync());
    }

    [PostgresFact]
    public async Task LateGeneralizedMessagesAreDiscarded()
    {
        var session = await CreateAsync();
        using var clear = await ClearAsync(session.Id, 1);
        var logger = new RecordingLogger<GeneralizedMessageHandler>();
        await using var scope = app.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<GeneralizedMessageHandler>(scope.ServiceProvider, logger);
        await handler.HandleAsync(new MessageEnvelope<GeneralizedCreatedMessage>(TestIds.From("late"),
            new(TestIds.From("late"), session.Id, 1, "Old question", [Guid.NewGuid(), Guid.NewGuid()])), CancellationToken.None);
        Assert.Contains("obsolete", Assert.Single(logger.Messages));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Faqs.ToArrayAsync());
    }

    [PostgresFact]
    public async Task MigrationPreservesLegacyRowsAndCanRunAgain()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var legacy = await db.Requests.Include(request => request.Messages).SingleAsync();
        Assert.Equal("Preserve this legacy request", legacy.Messages.First().Text.Value);
        Assert.StartsWith("legacy:", legacy.StudentId);
        Assert.Equal(Guid.Empty, legacy.SessionId);
        Assert.Equal(1, legacy.Generation);
        Assert.Equal(1, legacy.RequestNumber);
        Assert.Equal("BF-001", legacy.DisplayNumber);
        await db.Database.MigrateAsync();
        Assert.Single(await db.Requests.ToArrayAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.False(scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Database.HasPendingModelChanges());
        Assert.False(scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>().Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task RequestNumberMigrationBackfillsExistingRequestsAndContinuesTheCounter()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var sessionId = Guid.NewGuid();
        var first = HelpRequest.Create(sessionId, 1, "first", HelpType.Digital,
            new EirService.HelpRequests.Domain.ValueObjects.MessageText("First"), clock.Now);
        var second = HelpRequest.Create(sessionId, 1, "second", HelpType.Digital,
            new EirService.HelpRequests.Domain.ValueObjects.MessageText("Second"), clock.Now.AddMinutes(1));
        second.Cancel("second", AuthorRole.Student, clock.Now.AddMinutes(2));
        db.Requests.AddRange(first, second);
        await db.SaveChangesAsync();

        await db.GetService<IMigrator>().MigrateAsync("20261013073734_HelpRequestQueue");
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var existing = await db.Requests.Where(request => request.SessionId == sessionId)
            .OrderBy(request => request.CreatedAt).ToArrayAsync();
        Assert.Equal(new[] { 1, 2 }, existing.Select(request => request.RequestNumber));
        Assert.Equal(new[] { first.Id, second.Id }, existing.Select(request => request.Id));
        Assert.Equal(HelpRequestStatus.Cancelled, existing[1].Status);
        var next = HelpRequest.Create(sessionId, 1, "third", HelpType.Digital,
            new EirService.HelpRequests.Domain.ValueObjects.MessageText("Third"), clock.Now.AddMinutes(3));
        db.Requests.Add(next);
        await db.SaveChangesAsync();
        Assert.Equal(3, next.RequestNumber);
        Assert.Equal("BF-003", next.DisplayNumber);
    }

    [PostgresFact]
    public async Task SessionCreatorRenamePreservesExistingSessionsAndApiIdentity()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        var migrator = db.GetService<IMigrator>();
        const string previousMigration = "20261011102900_RemoveSessionOutbox";
        await migrator.MigrateAsync(previousMigration);

        var original = Session.Create("Existing session", new SessionCode("abcd2345"), new SessionCode("EFGH6789"),
            "provider|opaque-teacher", "Trusted Teacher", clock.Now);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sessions.sessions
                ("Id", "Title", "Code", "CreatedBySubjectId", "CreatedByDisplayName",
                 "CreatedAtUtc", "CurrentGeneration")
            VALUES ({original.Id}, {original.Title}, {original.Code.Value}, {original.CreatedByTeacherId},
                    {original.CreatedByDisplayName}, {original.CreatedAtUtc}, {original.CurrentGeneration})
            """);

        await db.Database.MigrateAsync();
        var migrated = await db.Sessions.AsNoTracking().SingleAsync();
        Assert.Equal(original.Id, migrated.Id);
        Assert.Equal(original.Title, migrated.Title);
        Assert.Equal(original.Code, migrated.Code);
        Assert.Equal(original.CreatedByTeacherId, migrated.CreatedByTeacherId);
        Assert.Equal(original.CreatedByDisplayName, migrated.CreatedByDisplayName);
        Assert.Equal(original.CreatedAtUtc, migrated.CreatedAtUtc);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::integer AS "Value" FROM pg_indexes
            WHERE schemaname = 'sessions' AND tablename = 'sessions'
              AND indexname = 'IX_sessions_CreatedByTeacherId'
            """).SingleAsync());

        var response = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{original.Id}");
        Assert.Equal(original.CreatedByTeacherId, response.GetProperty("createdByTeacherId").GetString());
        Assert.False(response.TryGetProperty("createdBySubjectId", out _));

        await migrator.MigrateAsync(previousMigration);
        Assert.Equal(original.CreatedByTeacherId, await db.Database.SqlQueryRaw<string>("""
            SELECT "CreatedBySubjectId" AS "Value" FROM sessions.sessions
            """).SingleAsync());
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        Assert.Equal(original.CreatedByTeacherId, (await db.Sessions.AsNoTracking().SingleAsync()).CreatedByTeacherId);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task PendingClearMessageMigratesToSessionUpdatedWithoutChangingItsIdentity()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261012065459_AddSessionClearedIntegrationOutbox");
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            eventId, sessionId, previousGeneration = 2, currentGeneration = 3, clearedAtUtc = clock.Now
        });
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sessions.outbox ("Id", "MessageId", "MessageType", "Payload", "ExchangeName", "RoutingKey", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {eventId.ToString()}, 'Shared.Application.Messaging.Contracts.SessionClearedIntegrationEvent',
                {payload}, 'sessions', 'session.cleared', {clock.Now});
            """);
        await db.Database.MigrateAsync();
        var pending = await db.Outbox.AsNoTracking().SingleAsync();
        var update = JsonSerializer.Deserialize<SessionUpdatedIntegrationEvent>(pending.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(eventId, pending.MessageId);
        Assert.Equal(typeof(SessionUpdatedIntegrationEvent).FullName, pending.MessageType);
        Assert.Equal(MessageRoutes.SessionUpdated, pending.RoutingKey);
        Assert.Equal(new SessionUpdatedIntegrationEvent(eventId, sessionId, 3, SessionUpdateType.Cleared, clock.Now, 2), update);
        Assert.Null(pending.PublishedAtUtc);
    }

    [PostgresFact]
    public async Task DeleteRemovesSessionAndCommitsDeletedEventWhileKeepingRequestsFromAllGenerations()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        await CreateRequestAsync(session.Id, 1, TestIds.From("delete-generation-1"));
        using var clear = await ClearAsync(session.Id, 1);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        await CreateRequestAsync(session.Id, 2, TestIds.From("delete-generation-2"));
        clock.Now = clock.Now.AddMinutes(2);
        using var response = await teacher.DeleteAsync($"/api/teachers/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        using var get = await teacher.GetAsync($"/api/teachers/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Empty((await teacher.GetFromJsonAsync<SessionResult[]>("/api/teachers/sessions"))!);
        Assert.Empty((await student.GetFromJsonAsync<HelpRequestResult[]>("/api/students/requests"))!);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        Assert.Empty(await db.Sessions.ToArrayAsync());
        var messages = await db.Outbox.AsNoTracking().ToArrayAsync();
        var deleted = Assert.Single(messages, message =>
            JsonSerializer.Deserialize<SessionUpdatedIntegrationEvent>(message.Payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!.EventType == SessionUpdateType.Deleted);
        var payload = JsonSerializer.Deserialize<SessionUpdatedIntegrationEvent>(deleted.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(session.Id, payload.SessionId);
        Assert.Equal(2, payload.Generation);
        Assert.Equal(clock.Now, payload.UpdatedAtUtc);
        Assert.Null(payload.PreviousGeneration);
        Assert.Equal(payload.EventId, deleted.MessageId);
        Assert.Equal(MessageRoutes.Sessions, deleted.ExchangeName);
        Assert.Equal(MessageRoutes.SessionUpdated, deleted.RoutingKey);
        Assert.Null(deleted.PublishedAtUtc);
        var requests = await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Requests
            .Where(request => request.SessionId == session.Id).ToArrayAsync();
        Assert.Equal(new[] { 1, 2 }, requests.Select(request => request.Generation).OrderBy(generation => generation));
        using var repeated = await teacher.DeleteAsync($"/api/teachers/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
        Assert.Equal(messages.Length, await db.Outbox.CountAsync());
    }

    [PostgresFact]
    public async Task DeleteRejectsAnotherTeacherMissingSessionAndEmptyIdWithoutChangingData()
    {
        var session = await CreateAsync();
        teacher.DefaultRequestHeaders.Remove("X-Test-Subject");
        teacher.DefaultRequestHeaders.Add("X-Test-Subject", "other-teacher");
        using var wrongOwner = await teacher.DeleteAsync($"/api/teachers/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.NotFound, wrongOwner.StatusCode);
        using var missing = await teacher.DeleteAsync($"/api/teachers/sessions/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var invalid = await teacher.DeleteAsync($"/api/teachers/sessions/{Guid.Empty}");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        Assert.Equal(session.Id, (await db.Sessions.SingleAsync()).Id);
        Assert.Empty(await db.Outbox.ToArrayAsync());
    }

    [PostgresFact]
    public async Task DeleteRequiresAuthenticatedTeacher()
    {
        var session = await CreateAsync();
        using var anonymous = app.GetTestClient();
        using var unauthenticated = await anonymous.DeleteAsync($"/api/teachers/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using var studentCaller = app.GetTestClient();
        studentCaller.DefaultRequestHeaders.Add("X-Test-Role", "Student");
        studentCaller.DefaultRequestHeaders.Add("X-Test-Subject", "student");
        studentCaller.DefaultRequestHeaders.Add("X-Test-Scope", "Bifrost");
        using var forbidden = await studentCaller.DeleteAsync($"/api/teachers/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Sessions.CountAsync());
    }

    [PostgresFact]
    public async Task FailedDeletedEventInsertRollsBackSessionRemovalAndCanRetry()
    {
        var created = await CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE sessions.outbox ADD CONSTRAINT "RejectDeletedForTest" CHECK ("Payload"::jsonb->>'eventType' <> 'Deleted');
            """);
        var repository = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var session = (await repository.GetAsync(created.Id, CancellationToken.None))!;
        session.Delete(clock.Now);
        repository.Remove(session);
        var domainEvent = Assert.Single(session.GetDomainEvents());
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<ISessionRepository>>();
        await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.SaveChangesAsync());
        Assert.Single(session.GetDomainEvents());
        await using (var verification = app.Services.CreateAsyncScope())
        {
            var saved = verification.ServiceProvider.GetRequiredService<EirSessionDbContext>();
            Assert.Equal(created.Id, (await saved.Sessions.SingleAsync()).Id);
            Assert.Empty(await saved.Outbox.ToArrayAsync());
        }
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE sessions.outbox DROP CONSTRAINT "RejectDeletedForTest";
            """);
        await unitOfWork.SaveChangesAsync();
        Assert.Empty(session.GetDomainEvents());
        Assert.Empty(await db.Sessions.ToArrayAsync());
        Assert.Equal(domainEvent.Id, (await db.Outbox.SingleAsync()).MessageId);
    }

    [PostgresFact]
    public async Task DeleteRejectsConcurrentClearWithoutQueuingDeletedEvent()
    {
        var created = await CreateAsync();
        await using var deleteScope = app.Services.CreateAsyncScope();
        var deleteRepository = deleteScope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var toDelete = (await deleteRepository.GetAsync(created.Id, CancellationToken.None))!;
        toDelete.Delete(clock.Now);
        deleteRepository.Remove(toDelete);
        using var cleared = await ClearAsync(created.Id, 1);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        await Assert.ThrowsAsync<UnitOfWorkConcurrencyException>(() =>
            deleteScope.ServiceProvider.GetRequiredService<IUnitOfWork<ISessionRepository>>().SaveChangesAsync());
        await using var verification = app.Services.CreateAsyncScope();
        var saved = verification.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        Assert.Equal(2, (await saved.Sessions.SingleAsync()).CurrentGeneration);
        var payload = JsonSerializer.Deserialize<SessionUpdatedIntegrationEvent>((await saved.Outbox.SingleAsync()).Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(SessionUpdateType.Cleared, payload.EventType);
    }

    [PostgresFact]
    public async Task ConcurrentTakeNextReservesDifferentRequestsAndQueueProtectsStudentPrivacy()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        await CreateRequestAsync(session.Id, 1, TestIds.From("queue-first"));
        using var secondStudent = await OtherStudentAsync(session.Code);
        var secondId = await SubmitAsync(secondStudent, session.Id, HelpType.Digital);
        var firstId = createdRequestIds[TestIds.From("queue-first")];
        var queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
        var expected = new[] { firstId, secondId }.OrderBy(id => id).ToArray();
        Assert.Equal(expected, queue.GetProperty("requests").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
        var ownQueue = await student.GetFromJsonAsync<JsonElement>($"/api/students/sessions/{session.Id}/queue");
        Assert.Equal(2, ownQueue.GetProperty("waitingCount").GetInt32());
        Assert.False(ownQueue.TryGetProperty("requests", out _));
        Assert.False(ownQueue.TryGetProperty("activeRequests", out _));
        Assert.Equal(Array.IndexOf(expected, firstId) + 1, ownQueue.GetProperty("position").GetInt32());
        Assert.Equal(Array.IndexOf(expected, firstId) * 5, ownQueue.GetProperty("estimatedWaitMinutes").GetInt32());

        var responses = await Task.WhenAll(
            teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null),
            teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null));
        var taken = new List<Guid>();
        foreach (var response in responses)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                taken.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
            }
        }
        Assert.Equal(expected, taken.OrderBy(id => id));
        using var empty = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);
        var after = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
        Assert.Equal(2, after.GetProperty("waitingCount").GetInt32()); // Reservation is still Pending.
    }

    [PostgresFact]
    public async Task OnlyAnEmptyWaitingQueueNotifiesItsSessionOwner()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Digital);
        var first = Assert.Single(await ReadNotificationsAsync());
        Assert.Equal(new NotificationCreatedMessage("provider|opaque-teacher", "Køen er startet. Der er en ny hjælpeanmodning.",
            session.Id, 1, firstId, "Created", "Pending"), first.Message);
        Assert.NotEqual(firstId, first.Id);

        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, take.StatusCode);
        using var otherStudent = await OtherStudentAsync(session.Code);
        var secondId = await SubmitAsync(otherStudent, session.Id, HelpType.Physical);
        Assert.Single(await ReadNotificationsAsync()); // A reserved Pending request still occupies the queue.

        using var cancelFirst = await student.PatchAsJsonAsync($"/api/students/requests/{firstId}/status", new { Action = "Cancel" });
        using var cancelSecond = await otherStudent.PatchAsJsonAsync($"/api/students/requests/{secondId}/status", new { Action = "Cancel" });
        Assert.Equal(HttpStatusCode.OK, cancelFirst.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cancelSecond.StatusCode);
        await SubmitAsync(student, session.Id, HelpType.Physical);
        Assert.Equal(2, (await ReadNotificationsAsync()).Length);

        using var cleared = await ClearAsync(session.Id, 1);
        cleared.EnsureSuccessStatusCode();
        await SubmitAsync(student, session.Id, HelpType.Digital);
        var notifications = await ReadNotificationsAsync();
        Assert.Equal(3, notifications.Length);
        Assert.All(notifications, notification => Assert.Equal("provider|opaque-teacher", notification.Message.UserId));
        Assert.Single(notifications, notification => notification.Message.Generation == 2);
    }

    [PostgresFact]
    public async Task ConcurrentStudentsEnteringAnEmptyQueueNotifyTheTeacherOnce()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var otherStudent = await OtherStudentAsync(session.Code);
        var ids = await Task.WhenAll(
            SubmitAsync(student, session.Id, HelpType.Digital),
            SubmitAsync(otherStudent, session.Id, HelpType.Physical));
        Assert.Equal(2, ids.Distinct().Count());
        var notification = Assert.Single(await ReadNotificationsAsync());
        Assert.Contains(notification.Message.HelpRequestId, ids.Select(id => (Guid?)id));
        Assert.Equal(new NotificationCreatedMessage("provider|opaque-teacher", "Køen er startet. Der er en ny hjælpeanmodning.",
            session.Id, 1, notification.Message.HelpRequestId, "Created", "Pending"), notification.Message);
    }

    [PostgresFact]
    public async Task FollowUpNotifiesOwnerOnlyWhenItIsTheFirstWaitingRequest()
    {
        const string owner = "provider|opaque-teacher";
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var id = await SubmitAsync(student, session.Id, HelpType.Digital);
        using var firstFollowUp = await student.PostAsJsonAsync($"/api/students/requests/{id}/messages", new { Text = "More details" });
        Assert.Equal(HttpStatusCode.OK, firstFollowUp.StatusCode);
        Assert.Equal(2, (await ReadNotificationsAsync()).Count(item => item.Message.UserId == owner));

        using var other = await OtherStudentAsync(session.Code);
        var otherId = await SubmitAsync(other, session.Id, HelpType.Physical);
        using var behindOther = await student.PostAsJsonAsync($"/api/students/requests/{id}/messages", new { Text = "Another detail" });
        Assert.Equal(HttpStatusCode.OK, behindOther.StatusCode);
        Assert.Equal(2, (await ReadNotificationsAsync()).Count(item => item.Message.UserId == owner));

        using var cancelOther = await other.PatchAsJsonAsync($"/api/students/requests/{otherId}/status", new { Action = "Cancel" });
        Assert.Equal(HttpStatusCode.OK, cancelOther.StatusCode);
        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, take.StatusCode);
        using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{id}/messages", new { Text = "Answer" });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        using var reopened = await student.PostAsJsonAsync($"/api/students/requests/{id}/messages", new { Text = "Follow-up after answer" });
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        var notifications = (await ReadNotificationsAsync()).Where(item => item.Message.UserId == owner).ToArray();
        Assert.Equal(3, notifications.Length);
        var followUps = notifications.Where(item => item.Message.Messages == "Køen er opdateret. Der er en opfølgende besked.").ToArray();
        Assert.Equal(2, followUps.Length);
        Assert.Equal(2, followUps.Select(item => item.Id).Distinct().Count());
        Assert.All(followUps, item => Assert.Equal(session.Id, item.Message.SessionId));
        Assert.All(followUps, item => Assert.Equal(id, item.Message.HelpRequestId));
        Assert.All(followUps, item => Assert.Equal("StudentMessageAdded", item.Message.Change));
        Assert.All(followUps, item => Assert.Equal("Pending", item.Message.Status));
    }

    [PostgresFact]
    public async Task ConcurrentFollowUpsIntoAnEmptyQueueNotifyOwnerOnce()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var other = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Digital);
        var secondId = await SubmitAsync(other, session.Id, HelpType.Digital);
        for (var index = 0; index < 2; index++)
        {
            using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
            Assert.Equal(HttpStatusCode.OK, take.StatusCode);
            var id = (await take.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{id}/messages", new { Text = "Answer" });
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        }
        var responses = await Task.WhenAll(
            student.PostAsJsonAsync($"/api/students/requests/{firstId}/messages", new { Text = "First follow-up" }),
            other.PostAsJsonAsync($"/api/students/requests/{secondId}/messages", new { Text = "Second follow-up" }));
        foreach (var response in responses)
        {
            using (response) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var notification = Assert.Single(await ReadNotificationsAsync(),
            item => item.Message.Messages == "Køen er opdateret. Der er en opfølgende besked.");
        Assert.Equal("provider|opaque-teacher", notification.Message.UserId);
    }

    [PostgresFact]
    public async Task PhysicalHelpLeavesQueueOnStartAndCompletesThroughStatusEndpoint()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var id = await SubmitAsync(student, session.Id, HelpType.Physical);
        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, take.StatusCode);
        Assert.Single(await ReadNotificationsAsync()); // Taking the request does not notify the student yet.
        using var badAnswer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{id}/messages", new { Text = "Digital answer" });
        Assert.Equal(HttpStatusCode.Conflict, badAnswer.StatusCode);
        using var start = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{id}/status", new { Action = "Start" });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var started = await start.Content.ReadFromJsonAsync<JsonElement>();
        var studentId = started.GetProperty("studentId").GetString();
        var displayNumber = started.GetProperty("displayNumber").GetString();
        var turn = Assert.Single(await ReadNotificationsAsync(), notification => notification.Message.UserId == studentId);
        Assert.Equal($"Det er din tur til fysisk hjælp. {displayNumber}.", turn.Message.Messages);
        Assert.Equal(session.Id, turn.Message.SessionId);
        Assert.Equal(id, turn.Message.HelpRequestId);
        Assert.Equal("Started", turn.Message.Change);
        Assert.Equal("Active", turn.Message.Status);
        var queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
        Assert.Equal(0, queue.GetProperty("waitingCount").GetInt32());
        Assert.Single(queue.GetProperty("activeRequests").EnumerateArray());
        using var studentCancel = await student.PatchAsJsonAsync($"/api/students/requests/{id}/status", new { Action = "Cancel" });
        Assert.Equal(HttpStatusCode.Conflict, studentCancel.StatusCode);
        clock.Now = clock.Now.AddMinutes(4);
        using var complete = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{id}/status", new { Action = "Complete" });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.Equal(2, (await ReadNotificationsAsync()).Length);
        clock.Now = clock.Now.AddMinutes(1);
        using var followUp = await student.PostAsJsonAsync($"/api/students/requests/{id}/messages", new { Text = "Follow-up" });
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
        var reopened = await followUp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, reopened.GetProperty("id").GetGuid());
        Assert.Equal(clock.Now, reopened.GetProperty("queuedAt").GetDateTimeOffset());
        Assert.Equal(2, reopened.GetProperty("messages").GetArrayLength());
    }

    [PostgresFact]
    public async Task DigitalAnswerPersistsOrderedConversationAndOutboxAndFollowUpReopensIt()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var id = await SubmitAsync(student, session.Id, HelpType.Digital);
        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, take.StatusCode);
        using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{id}/messages", new { Text = "Answer" });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        var studentId = (await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("studentId").GetString();
        var answered = Assert.Single(await ReadNotificationsAsync(), notification => notification.Message.UserId == studentId);
        Assert.Equal(new NotificationCreatedMessage(studentId!, "Der er kommet svar på din hjælpeanmodning.",
            session.Id, 1, id, "Answered", "Completed"), answered.Message);
        var conversation = await student.GetFromJsonAsync<JsonElement>($"/api/students/requests/{id}");
        Assert.Equal("Completed", conversation.GetProperty("status").GetString());
        Assert.Equal(new[] { 1, 2 }, conversation.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("sequenceNumber").GetInt32()));
        clock.Now = clock.Now.AddMinutes(1);
        using var followUp = await student.PostAsJsonAsync($"/api/students/requests/{id}/messages", new { Text = "Follow-up" });
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var saved = await db.Requests.Include(request => request.Messages).SingleAsync(request => request.Id == id);
        Assert.Equal(HelpRequestStatus.Pending, saved.Status);
        Assert.Equal(new[] { 1, 2, 3 }, saved.Messages.OrderBy(message => message.SequenceNumber).Select(message => message.SequenceNumber));
        Assert.Contains(await db.Outbox.ToArrayAsync(), message => message.RoutingKey == MessageRoutes.HelpRequestAnswered);
        Assert.Empty(bus.Messages);

        using var takeAgain = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, takeAgain.StatusCode);
        using var answerAgain = await teacher.PostAsJsonAsync($"/api/teachers/requests/{id}/messages", new { Text = "Another answer" });
        Assert.Equal(HttpStatusCode.OK, answerAgain.StatusCode);
        var replies = (await ReadNotificationsAsync()).Where(notification => notification.Message.UserId == studentId).ToArray();
        Assert.Equal(2, replies.Length);
        Assert.Equal(2, replies.Select(notification => notification.Id).Distinct().Count());
        Assert.All(replies, notification => Assert.Equal("Der er kommet svar på din hjælpeanmodning.", notification.Message.Messages));
    }

    [PostgresFact]
    public async Task RoleEndpointsEnforceAuthenticationOwnershipAndSessionMembership()
    {
        var session = await CreateAsync();
        using var notJoined = await student.PostAsJsonAsync($"/api/students/sessions/{session.Id}/requests", new { Message = "Question", HelpType = HelpType.Digital });
        Assert.Equal(HttpStatusCode.NotFound, notJoined.StatusCode);
        await JoinAsync(session.Code);
        var id = await SubmitAsync(student, session.Id, HelpType.Digital);
        using var other = await OtherStudentAsync(session.Code);
        using var privateRequest = await other.GetAsync($"/api/students/requests/{id}");
        Assert.Equal(HttpStatusCode.NotFound, privateRequest.StatusCode);
        using var take = await student.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.Unauthorized, take.StatusCode);
        using var start = await student.PatchAsJsonAsync($"/api/students/requests/{id}/status", new { Action = "Start" });
        Assert.Equal(HttpStatusCode.BadRequest, start.StatusCode);
        using var arbitraryStatus = await student.PatchAsJsonAsync($"/api/students/requests/{id}/status", new { Status = "Completed" });
        Assert.Equal(HttpStatusCode.BadRequest, arbitraryStatus.StatusCode);
        using var faq = await student.PatchAsJsonAsync($"/api/students/requests/{id}/status", new { Action = "AcceptFaq" });
        Assert.Equal(HttpStatusCode.BadRequest, faq.StatusCode);
        using var wrongTeacher = app.GetTestClient();
        wrongTeacher.DefaultRequestHeaders.Add("X-Test-Role", "Teacher");
        wrongTeacher.DefaultRequestHeaders.Add("X-Test-Subject", "other-teacher");
        wrongTeacher.DefaultRequestHeaders.Add("X-Test-Scope", "Bifrost");
        using var queue = await wrongTeacher.GetAsync($"/api/teachers/sessions/{session.Id}/queue");
        Assert.Equal(HttpStatusCode.NotFound, queue.StatusCode);
        using var create = await teacher.PostAsJsonAsync($"/api/students/sessions/{session.Id}/requests", new { Message = "Question", HelpType = HelpType.Digital });
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
    }

    [PostgresFact]
    public async Task SimultaneousCreatesAllowOnlyOneOpenRequestAndCommitItsOutbox()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var responses = await Task.WhenAll(
            student.PostAsJsonAsync($"/api/students/sessions/{session.Id}/requests", new { Message = "First", HelpType = HelpType.Digital }),
            student.PostAsJsonAsync($"/api/students/sessions/{session.Id}/requests", new { Message = "Second", HelpType = HelpType.Digital }));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        Assert.Equal(1, await db.Requests.CountAsync(request => request.SessionId == session.Id));
        Assert.Equal(3, await db.Outbox.CountAsync());
    }

    [PostgresFact]
    public async Task ConcurrentMessagesAndReopeningAnotherOpenRequestReturnConflict()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var id = await SubmitAsync(student, session.Id, HelpType.Digital);
        await using var firstScope = app.Services.CreateAsyncScope();
        await using var secondScope = app.Services.CreateAsyncScope();
        var first = (await firstScope.ServiceProvider.GetRequiredService<IHelpRequestRepository>().GetAsync(id, CancellationToken.None))!;
        var second = (await secondScope.ServiceProvider.GetRequiredService<IHelpRequestRepository>().GetAsync(id, CancellationToken.None))!;
        first.AddStudentMessage(first.StudentId!, new EirService.HelpRequests.Domain.ValueObjects.MessageText("First update"), clock.Now);
        second.AddStudentMessage(second.StudentId!, new EirService.HelpRequests.Domain.ValueObjects.MessageText("Second update"), clock.Now);
        await firstScope.ServiceProvider.GetRequiredService<IUnitOfWork<IHelpRequestRepository>>().SaveChangesAsync();
        await Assert.ThrowsAsync<EirService.Contracts.Exceptions.OperationConflictException>(() =>
            secondScope.ServiceProvider.GetRequiredService<IUnitOfWork<IHelpRequestRepository>>().SaveChangesAsync());
        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{id}/messages", new { Text = "Answer" });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        await SubmitAsync(student, session.Id, HelpType.Digital);
        using var reopen = await student.PostAsJsonAsync($"/api/students/requests/{id}/messages", new { Text = "Reopen" });
        Assert.Equal(HttpStatusCode.Conflict, reopen.StatusCode);
        var saved = await student.GetFromJsonAsync<JsonElement>($"/api/students/requests/{id}");
        Assert.Equal("Completed", saved.GetProperty("status").GetString());
        Assert.Equal(3, saved.GetProperty("messages").GetArrayLength());
    }

    [PostgresFact]
    public async Task FailedStatusOutboxWriteRollsBackTheRequestTransition()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var id = await SubmitAsync(student, session.Id, HelpType.Physical);
        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE helprequests.outbox ADD CONSTRAINT "RejectStartedForTest"
            CHECK ("Payload"::jsonb->>'change' IS DISTINCT FROM 'Started');
            """);
        var request = (await scope.ServiceProvider.GetRequiredService<IHelpRequestRepository>().GetAsync(id, CancellationToken.None))!;
        request.Start(request.AssignedTeacherId!, clock.Now);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            scope.ServiceProvider.GetRequiredService<IUnitOfWork<IHelpRequestRepository>>().SaveChangesAsync());
        await using var verification = app.Services.CreateAsyncScope();
        var saved = verification.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        Assert.Equal(HelpRequestStatus.Pending, (await saved.Requests.SingleAsync(request => request.Id == id)).Status);
        Assert.Equal(4, await saved.Outbox.CountAsync());
    }

    [PostgresFact]
    public async Task TeacherStatusEndpointValidatesActionsAndReturnsTheRequestWithoutChangingTheConversation()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var id = await SubmitAsync(student, session.Id, HelpType.Physical);
        var path = $"/api/teachers/requests/{id}/status";
        using var arbitraryStatus = await teacher.PatchAsJsonAsync(path, new { Status = "Completed" });
        Assert.Equal(HttpStatusCode.BadRequest, arbitraryStatus.StatusCode);
        using var unknown = await teacher.PatchAsJsonAsync(path, new { Action = "Unknown" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        using var cancelled = await teacher.PatchAsJsonAsync(path, new { Action = "Cancel" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var saved = await student.GetFromJsonAsync<JsonElement>($"/api/students/requests/{id}");
        Assert.Equal("Pending", saved.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("assignedTeacherId").ValueKind);
        Assert.Single(saved.GetProperty("messages").EnumerateArray());
        var queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
        Assert.Equal(1, queue.GetProperty("waitingCount").GetInt32());
        using var retake = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(id, (await retake.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

    [PostgresFact]
    public async Task StudentCancellationAllowsAnotherRequestAndPreservesOwnership()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var id = await SubmitAsync(student, session.Id, HelpType.Digital);
        using var other = await OtherStudentAsync(session.Code);
        using var forbidden = await other.PatchAsJsonAsync($"/api/students/requests/{id}/status", new { Action = "Cancel" });
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        using var cancelled = await student.PatchAsJsonAsync($"/api/students/requests/{id}/status", new { Action = "Cancel" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", (await cancelled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        var queue = await student.GetFromJsonAsync<JsonElement>($"/api/students/sessions/{session.Id}/queue");
        Assert.Equal(0, queue.GetProperty("waitingCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, queue.GetProperty("ownRequestId").ValueKind);
        var nextId = await SubmitAsync(student, session.Id, HelpType.Digital);
        Assert.NotEqual(id, nextId);
    }

    [PostgresFact]
    public async Task TeacherReturnsActiveHelpAheadOfOlderWaitingRequestsAndLatestReturnComesFirst()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var olderStudent = await OtherStudentAsync(session.Code);
        var olderId = await SubmitAsync(olderStudent, session.Id, HelpType.Digital);
        clock.Now = clock.Now.AddMinutes(1);
        var id = await SubmitAsync(student, session.Id, HelpType.Physical);
        using var takeOlder = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        using var takeOwn = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        using var start = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{id}/status", new { Action = "Start" });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        clock.Now = clock.Now.AddMinutes(1);
        using var returnOlder = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{olderId}/status", new { Action = "Cancel" });
        Assert.Equal(HttpStatusCode.OK, returnOlder.StatusCode);
        clock.Now = clock.Now.AddMinutes(1);
        using var returnOwn = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{id}/status", new { Action = "Cancel" });
        Assert.Equal(HttpStatusCode.OK, returnOwn.StatusCode);
        var returned = await returnOwn.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", returned.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, returned.GetProperty("startedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, returned.GetProperty("closedAt").ValueKind);
        var queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
        Assert.Equal(new[] { id, olderId }, queue.GetProperty("requests").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
        Assert.Empty(queue.GetProperty("activeRequests").EnumerateArray());
        var ownQueue = await student.GetFromJsonAsync<JsonElement>($"/api/students/sessions/{session.Id}/queue");
        Assert.Equal(1, ownQueue.GetProperty("position").GetInt32());
        using var retake = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(id, (await retake.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(2, returned.GetProperty("requestNumber").GetInt32());
    }

    [PostgresFact]
    public async Task RequestNumbersAreUniqueForConcurrentCreatesAndRestartPerSessionAndGeneration()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var other = await OtherStudentAsync(session.Code);
        var ids = await Task.WhenAll(SubmitAsync(student, session.Id, HelpType.Digital), SubmitAsync(other, session.Id, HelpType.Digital));
        var queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
        var requests = queue.GetProperty("requests").EnumerateArray().ToArray();
        Assert.Equal(new[] { 1, 2 }, requests.Select(item => item.GetProperty("requestNumber").GetInt32()).Order());
        Assert.Equal(new[] { "BF-001", "BF-002" }, requests.Select(item => item.GetProperty("displayNumber").GetString()).Order());
        var studentRequests = await student.GetFromJsonAsync<JsonElement>($"/api/students/requests?sessionId={session.Id}");
        var own = studentRequests.EnumerateArray().Single();
        var ownQueue = await student.GetFromJsonAsync<JsonElement>($"/api/students/sessions/{session.Id}/queue");
        Assert.Equal(own.GetProperty("displayNumber").GetString(), ownQueue.GetProperty("displayNumber").GetString());
        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        var taken = await take.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(taken.GetProperty("id").GetGuid(), ids);
        Assert.Equal(requests.Single(item => item.GetProperty("id").GetGuid() == taken.GetProperty("id").GetGuid())
            .GetProperty("displayNumber").GetString(), taken.GetProperty("displayNumber").GetString());

        var anotherSession = await CreateAsync("Another session");
        await JoinAsync(anotherSession.Code);
        var anotherId = await SubmitAsync(student, anotherSession.Id, HelpType.Digital);
        var another = await student.GetFromJsonAsync<JsonElement>($"/api/students/requests/{anotherId}");
        Assert.Equal("BF-001", another.GetProperty("displayNumber").GetString());
        using var cleared = await ClearAsync(session.Id, 1);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var afterClearId = await SubmitAsync(student, session.Id, HelpType.Digital);
        var afterClear = await student.GetFromJsonAsync<JsonElement>($"/api/students/requests/{afterClearId}");
        Assert.Equal("BF-001", afterClear.GetProperty("displayNumber").GetString());
        Assert.Equal(2, afterClear.GetProperty("generation").GetInt32());
    }

    private async Task<HttpClient> OtherStudentAsync(string code)
    {
        var client = app.GetTestClient();
        var cookie = app.Services.GetRequiredService<StudentIdentityCookie>().Protect(Guid.NewGuid().ToString("N"));
        client.DefaultRequestHeaders.Add("Cookie", StudentCookieHandler.CookieName + "=" + cookie);
        using var join = await client.PostAsJsonAsync("/api/students/sessions/join", new { Code = code });
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        return client;
    }

    private async Task<(Guid Id, NotificationCreatedMessage Message)[]> ReadNotificationsAsync()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var messages = await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.AsNoTracking()
            .Where(message => message.RoutingKey == MessageRoutes.NotificationCreated).ToArrayAsync();
        return messages.Select(message => (message.MessageId, JsonSerializer.Deserialize<NotificationCreatedMessage>(
            message.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!)).ToArray();
    }

    private static async Task<Guid> SubmitAsync(HttpClient client, Guid sessionId, HelpType helpType)
    {
        using var response = await client.PostAsJsonAsync($"/api/students/sessions/{sessionId}/requests", new { Message = "Question", HelpType = helpType.ToString() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<SessionResult> CreateAsync(string title = "Session title")
    {
        using var response = await teacher.PostAsJsonAsync("/api/teachers/sessions", new { Title = title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionResult>())!;
    }
    private async Task JoinAsync(string code)
    {
        using var response = await student.PostAsJsonAsync("/api/students/sessions/join", new { Code = code, StudentId = "spoofed" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    private Task<HttpResponseMessage> ClearAsync(Guid id, int generation) =>
        teacher.PostAsJsonAsync($"/api/teachers/sessions/{id}/clear", new { ExpectedGeneration = generation });
    private async Task CreateRequestAsync(Guid sessionId, int generation, Guid fixtureId)
    {
        using var response = await student.PostAsJsonAsync($"/api/students/sessions/{sessionId}/requests",
            new { Message = "Please help", HelpType = HelpType.Digital, Generation = -1, Id = fixtureId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = payload.GetProperty("id").GetGuid();
        Assert.NotEqual(fixtureId, id);
        Assert.Equal(generation, payload.GetProperty("generation").GetInt32());
        createdRequestIds[fixtureId] = id;
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(adminConnectionString)) return;
        teacher?.Dispose();
        student?.Dispose();
        if (app is not null) await app.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 11, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class RecordingBus : IMessageBus
    {
        public bool Fail { get; set; }
        public List<(object Payload, string RoutingKey)> Messages { get; } = [];
        public Task PublishAsync(Guid id, object payload, string exchangeName, string routingKey, CancellationToken cancellationToken = default, string? messageType = null)
        {
            if (Fail) throw new InvalidOperationException("Simulated broker failure");
            Messages.Add((payload, routingKey));
            return Task.CompletedTask;
        }
        public Task<IAsyncDisposable> SubscribeAsync<T>(string queueName, string exchangeName, string routingKey,
            Func<MessageEnvelope<T>, CancellationToken, Task> handler, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
