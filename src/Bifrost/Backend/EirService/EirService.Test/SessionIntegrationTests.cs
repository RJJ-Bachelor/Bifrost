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
using EirService.Participants.Infrastructure;
using EirService.Participants.Infrastructure.Persistence;
using EirService.Sessions.Application.Features.Teacher.ClearSession;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using EirService.Sessions.Application.Features.Teacher.GetSession;
using EirService.Sessions.Application.Features.Teacher.GetSessions;
using EirService.Sessions.Application.Features.Student.GetStudentSession;
using EirService.Sessions.Domain.Entities;
using EirService.Sessions.Domain.ValueObjects;
using EirService.Sessions.Infrastructure;
using EirService.Sessions.Infrastructure.Persistence;
using EirService.Sessions.Infrastructure.Repositories;
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
public sealed class SessionIntegrationTests : IAsyncLifetime
{
    private string? adminConnectionString;
    private string databaseName = null!;
    private string connectionString = null!;
    private WebApplication app = null!;
    private HttpClient teacher = null!;
    private HttpClient student = null!;
    private readonly TestClock clock = new();
    private readonly RecordingBus bus = new();

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
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:eir"] = connectionString,
            ["ConnectionStrings:rabbitmq"] = "amqp://unused-in-tests"
        });
        builder.Services.AddHelpRequestsInfrastructure(builder.Configuration);
        builder.Services.AddSessionsInfrastructure(builder.Configuration);
        builder.Services.AddParticipantsInfrastructure(builder.Configuration);
        builder.Services.RemoveAll<IHostedService>();
        builder.Services.RemoveAll<IMessageBus>();
        builder.Services.AddSingleton<IMessageBus>(bus);
        builder.Services.RemoveAll<TimeProvider>();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(Api.Program).Assembly, typeof(CreateSessionCommandHandler).Assembly,
                typeof(CreateHelpRequestCommandHandler).Assembly, typeof(JoinSessionCommandHandler).Assembly];
        });
        builder.Services.AddTransient<IValidator<CreateSessionCommand>, CreateSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<ClearSessionCommand>, ClearSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<GetSessionQuery>, GetSessionQueryValidator>();
        builder.Services.AddTransient<IValidator<GetSessionsQuery>, GetSessionsQueryValidator>();
        builder.Services.AddTransient<IValidator<GetStudentSessionQuery>, GetStudentSessionQueryValidator>();
        builder.Services.AddTransient<IValidator<JoinSessionCommand>, JoinSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<CreateHelpRequestCommand>, CreateHelpRequestCommandValidator>();
        builder.Services.AddTransient<IValidator<GetHelpRequestsQuery>, GetHelpRequestsQueryValidator>();
        builder.Services.AddCommandMiddleware(options => options.Register(typeof(CommandValidationMiddleware<,>), typeof(CommandLoggingMiddleware<,>)));
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
            await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>().Database.MigrateAsync();
        }
        app.UseMiddleware<ValidationExceptionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseFastEndpoints(configuration => configuration.Endpoints.RoutePrefix = "api");
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
            CreatedAtUtc = DateTimeOffset.MinValue, Code = "ZZZZZZZZ", CurrentGeneration = 88
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
        using var deniedRequest = await student.PostAsJsonAsync("/api/students/createhelprequest", new
            { Id = "unjoined", Message = "Help", SessionId = session.Id, Generation = 1 });
        Assert.Equal(HttpStatusCode.NotFound, deniedRequest.StatusCode);
        await JoinAsync(session.Code.ToLowerInvariant());
        await JoinAsync(session.Code);
        using var response = await student.GetAsync($"/api/students/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("code", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createdBy", body, StringComparison.OrdinalIgnoreCase);
        var list = (await student.GetFromJsonAsync<StudentSessionSummary[]>("/api/students/sessions"))!;
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
        await CreateRequestAsync(session.Id, 1, "old-request");
        clock.Now = clock.Now.AddHours(1);
        using var response = await ClearAsync(session.Id, 1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cleared = (await response.Content.ReadFromJsonAsync<ClearSessionResult>())!;
        Assert.Equal(new ClearSessionResult(session.Id, 2, clock.Now), cleared);
        Assert.Empty((await student.GetFromJsonAsync<HelpRequestResult[]>($"/api/students/requests?sessionId={session.Id}"))!);
        Assert.Empty((await student.GetFromJsonAsync<HelpRequestResult[]>("/api/students/requests"))!);
        var after = (await teacher.GetFromJsonAsync<SessionResult>($"/api/teachers/sessions/{session.Id}"))!;
        Assert.Equal(session with { CurrentGeneration = 2, LastClearedAtUtc = clock.Now }, after);
        var studentSession = (await student.GetFromJsonAsync<StudentSessionSummary>($"/api/students/sessions/{session.Id}"))!;
        Assert.Equal(2, studentSession.CurrentGeneration);
        using var stale = await student.PostAsJsonAsync("/api/students/createhelprequest", new
            { Id = "stale-request", Message = "Late request", SessionId = session.Id, Generation = 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await CreateRequestAsync(session.Id, 2, "new-request");
        var current = (await student.GetFromJsonAsync<HelpRequestResult[]>("/api/students/requests"))!;
        Assert.Equal("new-request", Assert.Single(current).Id);
        Assert.Equal(2, current[0].Generation);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var queued = await db.Outbox.SingleAsync(message => message.MessageId == "new-request" && message.RoutingKey == MessageRoutes.HelpRequestCreated);
        var payload = JsonSerializer.Deserialize<HelpRequestCreatedMessage>(queued.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
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
        var delayed = new HelpRequest("delayed", identity.UserId, "Old content", session.Id, 1);
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
        Assert.True(await new SessionRepository(firstDb).TryClearAsync(first, CancellationToken.None));
        Assert.False(await new SessionRepository(secondDb).TryClearAsync(second, CancellationToken.None));
        Assert.Equal(2, (await firstDb.Sessions.SingleAsync()).CurrentGeneration);
    }

    [PostgresFact]
    public async Task FailedOutboxInsertRollsBackHelpRequest()
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            var request = new HelpRequest("rollback-request", "student", "Help");
            OutgoingMessage[] messages = [new(request.Id, new HelpRequestCreatedMessage(request.Message), "requests", new string('x', 256))];
            await Assert.ThrowsAsync<DbUpdateException>(() => new HelpRequestRepository(db, clock).AddAsync(request, messages, CancellationToken.None));
        }
        await using var verification = app.Services.CreateAsyncScope();
        var saved = verification.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        Assert.False(await saved.Requests.AnyAsync(request => request.Id == "rollback-request"));
        Assert.Empty(await saved.Outbox.ToArrayAsync());
    }

    [PostgresFact]
    public async Task OutboxRetriesFailureAndPublishesBothContracts()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        await CreateRequestAsync(session.Id, 1, "queued-request");
        Assert.Empty(bus.Messages);
        var dispatcher = new OutboxDispatcher<EirRequestDbContext>(app.Services.GetRequiredService<IServiceScopeFactory>(), bus, clock,
            NullLogger<OutboxDispatcher<EirRequestDbContext>>.Instance);
        bus.Fail = true;
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        await using (var scope = app.Services.CreateAsyncScope())
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.CountAsync(message => message.PublishedAtUtc == null));
        bus.Fail = false;
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Contains(bus.Messages, message => message.RoutingKey == MessageRoutes.HelpRequestCreated);
        Assert.Contains(bus.Messages, message => message.RoutingKey == MessageRoutes.NotificationCreated);
        var help = (JsonElement)bus.Messages.Single(message => message.RoutingKey == MessageRoutes.HelpRequestCreated).Payload;
        Assert.Equal(session.Id, help.GetProperty("sessionId").GetGuid());
        Assert.Equal(1, help.GetProperty("generation").GetInt32());
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Equal(2, bus.Messages.Count);
    }

    [PostgresFact]
    public async Task CodeCollisionDoesNotPersistSecondSession()
    {
        var original = await CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        var collision = Session.Create("Collision", new SessionCode(original.Code), "teacher", null, clock.Now);
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
        var handler = new GeneralizedMessageHandler(logger);
        await handler.HandleAsync(new MessageEnvelope<GeneralizedCreatedMessage>("late", new("Old answer", session.Id, 1)), CancellationToken.None);
        Assert.Contains("Discarded", Assert.Single(logger.Messages));
        logger.Messages.Clear();
        await handler.HandleAsync(new MessageEnvelope<GeneralizedCreatedMessage>("new", new("New answer", session.Id, 2)), CancellationToken.None);
        Assert.Contains("New answer", Assert.Single(logger.Messages));
    }

    [PostgresFact]
    public async Task MigrationPreservesLegacyRowsAndCanRunAgain()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var legacy = await db.Requests.SingleAsync();
        Assert.Equal("Preserve this legacy request", legacy.Message);
        Assert.Null(legacy.UserId);
        Assert.Null(legacy.SessionId);
        Assert.Null(legacy.Generation);
        await db.Database.MigrateAsync();
        Assert.Single(await db.Requests.ToArrayAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.False(scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Database.HasPendingModelChanges());
        Assert.False(scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>().Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task SessionCreatorRenamePreservesExistingSessionsAndApiIdentity()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        var migrator = db.GetService<IMigrator>();
        const string previousMigration = "20261011102900_RemoveSessionOutbox";
        await migrator.MigrateAsync(previousMigration);

        var original = Session.Create("Existing session", new SessionCode("abcd2345"),
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
    private async Task CreateRequestAsync(Guid sessionId, int generation, string id)
    {
        using var response = await student.PostAsJsonAsync("/api/students/createhelprequest", new { Id = id, Message = "Please help", SessionId = sessionId, Generation = generation });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
        public Task PublishAsync(string id, object payload, string exchangeName, string routingKey, CancellationToken cancellationToken = default, string? messageType = null)
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
