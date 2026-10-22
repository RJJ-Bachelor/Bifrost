using EirService.HelpRequests.Application;
using EirService.HelpRequests.Domain.DomainEvents;
using EirService.HelpRequests.Domain.ValueObjects;
using Shared.Application.DomainEvents;
using FullHelpRequestResult = EirService.HelpRequests.Application.Features.Common.HelpRequestResult;
using Shared.Application.Persistence;
using Shared.Api.Authentication;
using EirService.Sessions.Application.Features.Teacher.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EirService.Api.Authentication;
using EirService.Api.Endpoints.Student.Participants.GetStudentIdentity;
using EirService.Api.Endpoints.Teacher.Participants.GetTeacherIdentity;
using EirService.Api.Middleware;
using EirService.Api.Middleware.Commands;
using EirService.HelpRequests.Application.Features.Student.CreateHelpRequest;
using EirService.HelpRequests.Application.Features.Student.GetHelpRequests;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using EirService.Sessions.Application.Repositories;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Application.Features.Teacher.JoinSession;
using EirService.Contracts.Participants.GetSessionTeacherIds;
using EirService.Contracts.Sessions.GetHelpRequestSession;
using EirService.Participants.Application.Features.Internal.GetSessionTeacherIds;
using EirService.Sessions.Application.Features.Internal.GetHelpRequestSession;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace EirService.Test;

[Collection("FastEndpoints")]
public sealed partial class CommandBusTests : IAsyncLifetime
{
    private readonly List<string> _executionOrder = [];
    private RecordingLogger<CreateSessionCommandHandler> _logger = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private RecordingRequestServices _requests = null!;

    public async Task InitializeAsync()
    {
        _logger = new RecordingLogger<CreateSessionCommandHandler>(_executionOrder);
        var commandLogger = new RecordingLogger<CommandLoggingMiddleware<CreateSessionCommand, SessionResult>>(_executionOrder);
        var helpRequestLogger = new RecordingLogger<CommandLoggingMiddleware<CreateHelpRequestCommand, FullHelpRequestResult>>(_executionOrder);
        var queryLogger = new RecordingLogger<CommandLoggingMiddleware<GetHelpRequestsQuery, IReadOnlyList<HelpRequestResult>>>(_executionOrder);
        _requests = new RecordingRequestServices(_executionOrder);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders().AddConsole();
        builder.WebHost.UseTestServer();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies =
            [
                typeof(Api.Program).Assembly,
                typeof(CreateSessionCommandHandler).Assembly,
                typeof(CreateHelpRequestCommandHandler).Assembly,
                typeof(JoinSessionCommandHandler).Assembly
            ];
        });
        builder.Services.AddTransient<IValidator<CreateSessionCommand>, CreateSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<JoinTeacherSessionCommand>, JoinTeacherSessionCommandValidator>();
        builder.Services.AddHelpRequestsApplication();
        builder.Services.AddCommandMiddleware(options => options.Register(
            typeof(CommandValidationMiddleware<,>),
            typeof(CommandLoggingMiddleware<,>)));
        builder.Services.AddSingleton<ILogger<CreateSessionCommandHandler>>(_logger);
        builder.Services.AddSingleton<ILogger<CommandLoggingMiddleware<CreateSessionCommand, SessionResult>>>(commandLogger);
        builder.Services.AddSingleton<ILogger<CommandLoggingMiddleware<CreateHelpRequestCommand, FullHelpRequestResult>>>(helpRequestLogger);
        builder.Services.AddSingleton<ILogger<CommandLoggingMiddleware<GetHelpRequestsQuery, IReadOnlyList<HelpRequestResult>>>>(queryLogger);
        builder.Services.AddSingleton<IHelpRequestRepository>(_requests);
        builder.Services.AddSingleton<IUnitOfWork<IHelpRequestRepository>>(_requests);
        builder.Services.AddSingleton<MemorySessionRepository>();
        builder.Services.AddSingleton<ISessionRepository>(provider => provider.GetRequiredService<MemorySessionRepository>());
        builder.Services.AddSingleton<IUnitOfWork<ISessionRepository>>(provider => provider.GetRequiredService<MemorySessionRepository>());
        builder.Services.AddSingleton<IParticipantRepository, MemoryParticipantRepository>();
        builder.Services.AddScoped<ICommandHandler<GetSessionTeacherIdsQuery, string[]>, GetSessionTeacherIdsQueryHandler>();
        builder.Services.AddScoped<ICommandHandler<GetHelpRequestSessionQuery, HelpRequestSession?>, GetHelpRequestSessionQueryHandler>();
        builder.Services.AddSingleton(TimeProvider.System);
        var dataProtection = new EphemeralDataProtectionProvider();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                JwtBearerDefaults.AuthenticationScheme, _ => { })
            .AddStudentCookie(builder.Configuration, options => options.DataProtectionProvider = dataProtection);
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Student", policy => policy
                .AddAuthenticationSchemes(StudentCookieHandler.SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim("sub")
                .RequireRole("Student"));
            options.AddPolicy("Teacher", policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("sub")
                .RequireRole("Teacher")
                .RequireAssertion(context => context.User.FindAll("scope")
                    .Any(claim => claim.Value.Split(' ').Contains("Bifrost"))));
        });

        builder.Services.RemoveAll<IDataProtectionProvider>();
        builder.Services.AddSingleton<IDataProtectionProvider>(dataProtection);
        // This in-memory fixture needs no background services. DataProtection's key-ring
        // warmup otherwise accesses the host key store even with an ephemeral provider.
        builder.Services.RemoveAll<IHostedService>();

        // Exercise the endpoints and application slices with recorded infrastructure calls.
        _app = builder.Build();
        _app.UseMiddleware<ValidationExceptionMiddleware>();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseFastEndpoints(configuration => configuration.Endpoints.RoutePrefix = "api");
        _app.MapGet("/legacy-validation-error", IResult () => throw new ValidationException("Legacy validation"));
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [Fact]
    public async Task TeacherCanCreateSessionWithTitle()
    {
        Authenticate("Teacher", "teacher-123");

        using var response = await _client.PostAsJsonAsync("/api/teachers/sessions", new { Title = "Session title" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<SessionResult>();
        Assert.NotNull(created);
        Assert.Equal("Session title", created.Title);
        Assert.Equal("teacher-123", created.CreatedByTeacherId);
        Assert.Contains("teacher-123", Assert.Single(_logger.Messages));
        Assert.Equal(new[]
        {
            "Executing command CreateSessionCommand",
            "CreateSession handler called for teacher teacher-123",
            "Executed command CreateSessionCommand"
        }, _executionOrder);
    }

    [Fact]
    public async Task TeacherIdComesFromAuthenticatedClaims()
    {
        Authenticate("Teacher", "authenticated-teacher");

        using var response = await _client.PostAsJsonAsync("/api/teachers/sessions",
            new { Title = "Session title", TeacherId = "supplied-teacher" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var message = Assert.Single(_logger.Messages);
        Assert.Contains("authenticated-teacher", message);
        Assert.DoesNotContain("supplied-teacher", message);
    }

    [Fact]
    public async Task AnonymousRequestIsRejectedBeforeHandlerRuns()
    {
        using var response = await _client.PostAsJsonAsync("/api/teachers/sessions", new { Title = "Session title" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_logger.Messages);
    }

    [Theory]
    [InlineData("Student", "student-123", "Bifrost")]
    [InlineData("Teacher", null, "Bifrost")]
    [InlineData("Teacher", "teacher-123", "Other")]
    public async Task TeacherPolicyRejectsMissingRequirements(string role, string? teacherId, string scope)
    {
        Authenticate(role, teacherId, scope);

        using var response = await _client.PostAsJsonAsync("/api/teachers/sessions", new { Title = "Session title" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_logger.Messages);
    }

    [Fact]
    public async Task ValidationReturnsBadRequestBeforeSuccessIsLogged()
    {
        Authenticate("Teacher", string.Empty);

        using var response = await _client.PostAsJsonAsync("/api/teachers/sessions", new { Title = "Session title" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("TeacherId", await response.Content.ReadAsStringAsync());
        Assert.Empty(_logger.Messages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CommandBusRejectsInvalidCommandsBeforeCallingHandler(string? teacherId)
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            new CreateSessionCommand(teacherId!, "Session title").ExecuteAsync(CancellationToken.None));

        Assert.Equal(nameof(CreateSessionCommand.TeacherId), Assert.Single(exception.Errors).PropertyName,
            ignoreCase: true);
        Assert.Empty(_executionOrder);
    }

    [Fact]
    public async Task CancelledCommandStopsBeforeLoggingAndHandler()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CreateSessionCommand("teacher-123", "Session title").ExecuteAsync(cancellation.Token));

        Assert.Empty(_executionOrder);
    }

    [Fact]
    public async Task CommandBusRunsMiddlewareWithoutHttpRequest()
    {
        var result = await new CreateSessionCommand("teacher-123", "Session title").ExecuteAsync(CancellationToken.None);

        Assert.Equal("Session title", result.Title);
        Assert.Equal(new[]
        {
            "Executing command CreateSessionCommand",
            "CreateSession handler called for teacher teacher-123",
            "Executed command CreateSessionCommand"
        }, _executionOrder);
    }

    [Fact]
    public async Task ValidationMiddlewareLeavesMinimalApiExceptionsUnhandled()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _client.GetAsync("/legacy-validation-error"));
    }

    [Fact]
    public async Task CommandValidationCollectsEachValidatorsErrorsWithoutCallingHandler()
    {
        var additionalValidator = new InlineValidator<CreateSessionCommand>();
        additionalValidator.RuleFor(command => command.TeacherId)
            .MinimumLength(3).WithMessage("TeacherIdTooShort");
        var middleware = new CommandValidationMiddleware<CreateSessionCommand, SessionResult>(
            [new CreateSessionCommandValidator(), additionalValidator]);
        var handlerCalled = false;

        var exception = await Assert.ThrowsAsync<ValidationException>(() => middleware.ExecuteAsync(
            new CreateSessionCommand(string.Empty, "Session title"),
            () =>
            {
                handlerCalled = true;
                return Task.FromResult<SessionResult>(null!);
            },
            CancellationToken.None));

        Assert.Equal(new[] { "TeacherId", "TeacherIdTooShort" }, exception.Errors.Select(error => error.ErrorMessage));
        Assert.False(handlerCalled);
    }

    [Fact]
    public async Task StudentCreatesServerOwnedRequestAndCannotCreateAnotherOpenRequest()
    {
        var studentId = AuthenticateStudent();
        var sessionId = await SeedSessionAsync(studentId);
        var suppliedId = Guid.NewGuid();
        using var response = await _client.PostAsJsonAsync($"/api/students/sessions/{sessionId}/requests",
            new { Id = suppliedId, StudentId = "spoofed", Generation = 99, Message = "Please help", HelpType = HelpType.Digital });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<FullHelpRequestResult>(
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }))!;
        Assert.NotEqual(suppliedId, result.Id);
        Assert.Equal(studentId, result.StudentId);
        Assert.Equal(1, result.Generation);
        Assert.Equal("Please help", Assert.Single(result.Messages).Text);
        Assert.Equal((result.Id, "Please help"), Assert.Single(_requests.RequestMessages));
        var notification = Assert.Single(_requests.Notifications);
        Assert.Equal(new NotificationCreatedMessage("teacher-123", "Køen er startet. Der er en ny hjælpeanmodning.",
            sessionId, 1, result.Id, "Created", "Pending"), notification.Message);
        Assert.NotEqual(result.Id, notification.Id);
        using var duplicate = await _client.PostAsJsonAsync($"/api/students/sessions/{sessionId}/requests",
            new { Message = "Another question", HelpType = HelpType.Physical });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Single(_requests.Saved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task HelpRequestValidationStopsEmptyMessagesBeforeSideEffects(string? message)
    {
        AuthenticateStudent();
        using var response = await _client.PostAsJsonAsync($"/api/students/sessions/{Guid.NewGuid()}/requests",
            new { Message = message, HelpType = HelpType.Digital });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_requests.Saved);
        Assert.Empty(_requests.RequestMessages);
    }

    [Fact]
    public async Task HelpRequestRequiresHelpTypeAndRejectsOverlongMessages()
    {
        AuthenticateStudent();
        var path = $"/api/students/sessions/{Guid.NewGuid()}/requests";
        using var missing = await _client.PostAsJsonAsync(path, new { Message = "Please help" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var longText = await _client.PostAsJsonAsync(path, new { Message = new string('m', 4001), HelpType = HelpType.Physical });
        Assert.Equal(HttpStatusCode.BadRequest, longText.StatusCode);
        Assert.Empty(_requests.Saved);
    }

    [Fact]
    public async Task CommandBusCreatesHelpRequestWithoutHttp()
    {
        var sessionId = await SeedSessionAsync("student-123");
        var result = await new CreateHelpRequestCommand(sessionId, "student-123", new string('m', 4000), HelpType.Digital)
            .ExecuteAsync(CancellationToken.None);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("student-123", Assert.Single(_requests.Saved).StudentId);
        Assert.Single(_requests.RequestMessages);
        var notification = Assert.Single(_requests.Notifications);
        Assert.Equal(new NotificationCreatedMessage("teacher-123", "Køen er startet. Der er en ny hjælpeanmodning.",
            sessionId, 1, result.Id, "Created", "Pending"), notification.Message);
    }

    [Fact]
    public async Task CancelledHelpRequestStopsBeforeLoggingAndSideEffects()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CreateHelpRequestCommand(Guid.NewGuid(), "student-123", "Please help", HelpType.Physical).ExecuteAsync(cancellation.Token));
        Assert.Empty(_requests.Saved);
        Assert.Empty(_executionOrder);
    }

    [Fact]
    public async Task HelpRequestEndpointsUseTheRoleSpecificIdentityWhenBothCredentialsArePresent()
    {
        var studentId = AuthenticateStudent();
        var sessionId = await SeedSessionAsync(studentId);
        Authenticate("Teacher", "teacher-123");
        using var created = await _client.PostAsJsonAsync($"/api/students/sessions/{sessionId}/requests",
            new { Message = "Student question", HelpType = HelpType.Digital });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var studentRequest = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(studentId, studentRequest.GetProperty("studentId").GetString());
        var id = studentRequest.GetProperty("id").GetGuid();
        using var teacherRead = await _client.GetAsync($"/api/teachers/requests/{id}");
        Assert.Equal(HttpStatusCode.OK, teacherRead.StatusCode);
        Assert.Equal(id, (await teacherRead.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid());
    }

    private async Task<Guid> SeedSessionAsync(string studentId)
    {
        var session = await new CreateSessionCommand("teacher-123", "Help session").ExecuteAsync(CancellationToken.None);
        await new JoinSessionCommand(session.Code, studentId).ExecuteAsync(CancellationToken.None);
        _executionOrder.Clear();
        return session.Id;
    }

    [Fact]
    public async Task MonitoringAliveRemainsAnonymousAndPreservesResponse()
    {
        using var response = await _client.GetAsync("/api/monitoring/alive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("EirService is alive and running.", await response.Content.ReadFromJsonAsync<string>());
        Assert.Empty(_requests.Saved);
        Assert.Empty(_requests.Reads);
    }

    [Fact]
    public async Task StudentIdentityValidatesGatewayCookieAndReusesIt()
    {
        var studentId = AuthenticateStudent();
        using var response = await _client.GetAsync("/api/students/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var identity = await response.Content.ReadFromJsonAsync<StudentIdentityResponse>();
        Assert.NotNull(identity);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"userId\":", body);
        Assert.Contains("\"roles\":", body);
        Assert.True(Guid.TryParseExact(identity.UserId, "N", out _));
        Assert.Equal(new[] { "Student" }, identity.Roles);
        Assert.Equal(studentId, identity.UserId);
        Assert.False(response.Headers.Contains("Set-Cookie"));

        using var nextResponse = await _client.GetAsync("/api/students/me?userId=supplied-student");

        Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode);
        var nextIdentity = await nextResponse.Content.ReadFromJsonAsync<StudentIdentityResponse>();
        Assert.NotNull(nextIdentity);
        Assert.Equal(identity.UserId, nextIdentity.UserId);
        Assert.False(nextResponse.Headers.Contains("Set-Cookie"));
        Assert.Empty(_requests.Reads);
        Assert.Empty(_requests.Saved);
    }

    [Fact]
    public async Task TeacherIdentityReturnsAuthenticatedSubjectNameAndRoles()
    {
        Authenticate("Teacher", "teacher-123");
        _client.DefaultRequestHeaders.Add("X-Test-Name", "Teacher Name");

        using var response = await _client.GetAsync("/api/teachers/me?userId=supplied-teacher");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var identity = await response.Content.ReadFromJsonAsync<TeacherIdentityResponse>();
        Assert.NotNull(identity);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"userId\":", body);
        Assert.Contains("\"name\":", body);
        Assert.Contains("\"roles\":", body);
        Assert.Equal("teacher-123", identity.UserId);
        Assert.Equal("Teacher Name", identity.Name);
        Assert.Equal(new[] { "Teacher" }, identity.Roles);
        Assert.Empty(_executionOrder);
    }

    [Fact]
    public async Task AnonymousTeacherIdentityIsRejected()
    {
        using var response = await _client.GetAsync("/api/teachers/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Student", "student-123", "Bifrost")]
    [InlineData("Teacher", null, "Bifrost")]
    [InlineData("Teacher", "teacher-123", "Other")]
    public async Task TeacherIdentityEnforcesExistingPolicy(string role, string? userId, string scope)
    {
        Authenticate(role, userId, scope);

        using var response = await _client.GetAsync("/api/teachers/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HelpRequestHistoryOnlyReturnsAuthenticatedStudentsCurrentRequests()
    {
        var studentId = AuthenticateStudent();
        var sessionId = await SeedSessionAsync(studentId);
        var mine = HelpRequest.Create(sessionId, 1, studentId, HelpType.Digital, new MessageText("My question"), DateTimeOffset.UtcNow);
        _requests.Saved.Add(mine);
        _requests.Saved.Add(HelpRequest.Create(sessionId, 1, "other-student", HelpType.Digital, new MessageText("Private"), DateTimeOffset.UtcNow));
        _requests.Saved.Add(HelpRequest.Create(sessionId, 2, studentId, HelpType.Digital, new MessageText("Wrong generation"), DateTimeOffset.UtcNow));
        using var response = await _client.GetAsync("/api/students/requests?userId=other-student");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = Assert.Single((await response.Content.ReadFromJsonAsync<HelpRequestResult[]>())!);
        Assert.Equal(mine.Id, result.Id);
        Assert.Equal(studentId, result.StudentId);
    }

    [Fact]
    public async Task HelpRequestHistoryReturnsEmptyArrayForNewStudent()
    {
        AuthenticateStudent();
        using var response = await _client.GetAsync("/api/students/requests");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task QueryUsesCommandBusOutsideHttpAndForwardsCancellationToken()
    {
        var sessionId = await SeedSessionAsync("student-123");
        var request = HelpRequest.Create(sessionId, 1, "student-123", HelpType.Physical, new MessageText("Please help"), DateTimeOffset.UtcNow);
        _requests.Saved.Add(request);
        using var cancellation = new CancellationTokenSource();
        var results = await new GetHelpRequestsQuery("student-123").ExecuteAsync(cancellation.Token);
        Assert.Equal(request.Id, Assert.Single(results).Id);
        Assert.Equal(cancellation.Token, Assert.Single(_requests.Reads).CancellationToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task QueryValidationStopsInvalidSubjectsBeforeRepository(string? userId)
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            new GetHelpRequestsQuery(userId!).ExecuteAsync(CancellationToken.None));

        Assert.Equal(nameof(GetHelpRequestsQuery.UserId), Assert.Single(exception.Errors).PropertyName, ignoreCase: true);
        Assert.Empty(_requests.Reads);
        Assert.Empty(_executionOrder);
    }

    [Fact]
    public async Task CancelledQueryStopsBeforeRepositoryAndLogging()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GetHelpRequestsQuery("student-123").ExecuteAsync(cancellation.Token));

        Assert.Empty(_requests.Reads);
        Assert.Empty(_executionOrder);
    }

    internal sealed class RecordingRequestServices(List<string> executionOrder)
        : IHelpRequestRepository, IUnitOfWork<IHelpRequestRepository>
    {
        public List<HelpRequest> Saved { get; } = [];
        public List<(string UserId, CancellationToken CancellationToken)> Reads { get; } = [];
        public List<(Guid Id, string Message)> RequestMessages { get; } = [];
        public List<(Guid Id, NotificationCreatedMessage Message)> Notifications { get; } = [];
        public Task AddAsync(HelpRequest request, CancellationToken cancellationToken)
        {
            Saved.Add(request);
            return Task.CompletedTask;
        }
        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var request in Saved)
            {
                foreach (var domainEvent in request.GetDomainEvents().OfType<HelpRequestChangedDomainEvent>())
                    await new DomainEventNotification<HelpRequestChangedDomainEvent>(domainEvent, outgoing =>
                    {
                        if (outgoing.Payload is HelpRequestCreatedMessage help) RequestMessages.Add((outgoing.Id, help.Message));
                        if (outgoing.Payload is NotificationCreatedMessage notification) Notifications.Add((outgoing.Id, notification));
                    }).PublishAsync(Mode.WaitForAll, cancellationToken);
                request.ClearDomainEvents();
            }
        }
        public Task<HelpRequest?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Saved.SingleOrDefault(request => request.Id == id));
        public Task<IReadOnlyList<HelpRequest>> GetForSessionAsync(Guid sessionId, int generation, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HelpRequest>>(Saved.Where(request => request.SessionId == sessionId && request.Generation == generation)
                .OrderByDescending(request => request.ReturnedToQueueAt)
                .ThenBy(request => request.QueuedAt).ThenBy(request => request.Id).ToArray());
        public Task<IReadOnlyList<HelpRequest>> GetByIdsAsync(Guid[] ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HelpRequest>>(Saved.Where(request => ids.Contains(request.Id)).ToArray());
        public Task<IReadOnlyList<HelpRequest>> GetLinkedToFaqAsync(Guid faqId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HelpRequest>>(Saved.Where(request => !request.IsFaq && request.FaqAnswerId == faqId).ToArray());
        public Task<bool> HasOpenAsync(Guid sessionId, int generation, string studentId, CancellationToken cancellationToken) =>
            Task.FromResult(Saved.Any(request => request.SessionId == sessionId && request.Generation == generation && request.StudentId == studentId && request.IsOpen));
        public Task<HelpRequest?> TakeNextAsync(Guid sessionId, int generation, string teacherId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Reservation is tested against PostgreSQL.");
        public Task<IReadOnlyList<HelpRequest>> GetByUserIdAsync(string userId, CancellationToken cancellationToken)
        {
            Reads.Add((userId, cancellationToken));
            executionOrder.Add($"Read help requests for {userId}");
            return Task.FromResult<IReadOnlyList<HelpRequest>>(Saved.Where(request => request.StudentId == userId).OrderBy(request => request.Id).ToArray());
        }
    }

    private string AuthenticateStudent()
    {
        var userId = Guid.NewGuid().ToString("N");
        var cookie = _app.Services.GetRequiredService<StudentIdentityCookie>().Protect(userId);
        _client.DefaultRequestHeaders.Add("Cookie", $"{StudentCookieHandler.CookieName}={cookie}");
        return userId;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("altered-cookie")]
    public async Task DirectStudentCallWithoutValidCookieIsRejectedWithoutIssuingOne(string? cookie)
    {
        if (cookie is not null)
            _client.DefaultRequestHeaders.Add("Cookie", $"{StudentCookieHandler.CookieName}={cookie}");
        using var response = await _client.GetAsync("/api/students/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Empty(_requests.Reads);
        Assert.Empty(_requests.Saved);
    }

    private void Authenticate(string role, string? teacherId, string scope = "Bifrost")
    {
        _client.DefaultRequestHeaders.Add("X-Test-Role", role);
        _client.DefaultRequestHeaders.Add("X-Test-Scope", scope);
        if (teacherId == string.Empty)
        {
            _client.DefaultRequestHeaders.Add("X-Test-Empty-Subject", "true");
        }
        else if (teacherId is not null)
        {
            _client.DefaultRequestHeaders.Add("X-Test-Subject", teacherId);
        }
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private sealed class RecordingLogger<TCategory>(List<string> executionOrder) : ILogger<TCategory>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            Messages.Add(message);
            executionOrder.Add(message);
        }
    }

    internal sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            List<Claim> claims = [new("role", role.ToString()), new("scope", Request.Headers["X-Test-Scope"].ToString())];
            if (Request.Headers.TryGetValue("X-Test-Name", out var name))
            {
                claims.Add(new Claim("name", name.ToString()));
            }
            if (Request.Headers.ContainsKey("X-Test-Empty-Subject"))
            {
                claims.Add(new Claim("sub", string.Empty));
            }
            else if (Request.Headers.TryGetValue("X-Test-Subject", out var subject))
            {
                claims.Add(new Claim("sub", subject.ToString()));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name, "name", "role"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
