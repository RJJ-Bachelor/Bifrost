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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace EirService.Test;

[Collection("FastEndpoints")]
public sealed class CommandBusTests : IAsyncLifetime
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
        var helpRequestLogger = new RecordingLogger<CommandLoggingMiddleware<CreateHelpRequestCommand, Guid>>(_executionOrder);
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
        builder.Services.AddTransient<IValidator<CreateHelpRequestCommand>, CreateHelpRequestCommandValidator>();
        builder.Services.AddTransient<IValidator<GetHelpRequestsQuery>, GetHelpRequestsQueryValidator>();
        builder.Services.AddCommandMiddleware(options => options.Register(
            typeof(CommandValidationMiddleware<,>),
            typeof(CommandLoggingMiddleware<,>)));
        builder.Services.AddSingleton<ILogger<CreateSessionCommandHandler>>(_logger);
        builder.Services.AddSingleton<ILogger<CommandLoggingMiddleware<CreateSessionCommand, SessionResult>>>(commandLogger);
        builder.Services.AddSingleton<ILogger<CommandLoggingMiddleware<CreateHelpRequestCommand, Guid>>>(helpRequestLogger);
        builder.Services.AddSingleton<ILogger<CommandLoggingMiddleware<GetHelpRequestsQuery, IReadOnlyList<HelpRequestResult>>>>(queryLogger);
        builder.Services.AddSingleton<IHelpRequestRepository>(_requests);
        builder.Services.AddSingleton<MemorySessionRepository>();
        builder.Services.AddSingleton<ISessionRepository>(provider => provider.GetRequiredService<MemorySessionRepository>());
        builder.Services.AddSingleton<IUnitOfWork<ISessionRepository>>(provider => provider.GetRequiredService<MemorySessionRepository>());
        builder.Services.AddSingleton<IParticipantRepository, MemoryParticipantRepository>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                JwtBearerDefaults.AuthenticationScheme, _ => { })
            .AddStudentCookie(builder.Configuration, options => options.DataProtectionProvider = new EphemeralDataProtectionProvider());
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
    public async Task StudentCanCreateHelpRequestUsingCookieIdentityAndCommandPipeline()
    {
        AuthenticateStudent();
        using var response = await _client.PostAsJsonAsync("/api/students/createhelprequest",
            new { Id = TestIds.From("request-123"), Message = "Please help", UserId = "supplied-student" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestIds.From("request-123"), await response.Content.ReadFromJsonAsync<Guid>());
        var saved = Assert.Single(_requests.Saved);
        Assert.True(Guid.TryParseExact(saved.UserId, "N", out _));
        Assert.NotEqual("supplied-student", saved.UserId);
        Assert.Equal(TestIds.From("request-123"), saved.Id);
        Assert.Equal("Please help", saved.Message);
        Assert.Equal((TestIds.From("request-123"), "Please help"), Assert.Single(_requests.RequestMessages));
        Assert.Equal((TestIds.From("request-123"), saved.UserId, "Please help"), Assert.Single(_requests.Notifications));
        Assert.Equal(new[]
        {
            "Executing command CreateHelpRequestCommand",
            $"Saved help request {TestIds.From("request-123")}",
            $"Queued help request {TestIds.From("request-123")}",
            $"Queued notification {TestIds.From("request-123")}",
            "Executed command CreateHelpRequestCommand"
        }, _executionOrder);

        Assert.False(response.Headers.Contains("Set-Cookie"));

        using var nextResponse = await _client.PostAsJsonAsync("/api/students/createhelprequest",
            new { Id = TestIds.From("request-456"), Message = "Another request" });

        Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode);
        Assert.Equal(saved.UserId, _requests.Saved[1].UserId);
    }

    public static TheoryData<string?, string?, string> InvalidHelpRequestBodies => new()
    {
        { null, "Please help", "Id" },
        { Guid.Empty.ToString(), "Please help", "Id" },
        { "not-a-guid", "Please help", "Id" },
        { "", "Please help", "Id" },
        { " ", "Please help", "Id" },
        { new string('i', 101), "Please help", "Id" },
        { TestIds.From("request-123").ToString(), null, "Message" },
        { TestIds.From("request-123").ToString(), "", "Message" },
        { TestIds.From("request-123").ToString(), " ", "Message" },
        { TestIds.From("request-123").ToString(), new string('m', 4001), "Message" }
    };

    [Theory]
    [MemberData(nameof(InvalidHelpRequestBodies))]
    public async Task HelpRequestValidationReturnsBadRequestBeforeSideEffects(string? id, string? message, string property)
    {
        AuthenticateStudent();
        using var response = await _client.PostAsJsonAsync("/api/students/createhelprequest", new { Id = id, Message = message });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(property, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_requests.Saved);
        Assert.Empty(_requests.RequestMessages);
        Assert.Empty(_requests.Notifications);
        Assert.Empty(_executionOrder);
    }

    [Fact]
    public async Task HelpRequestAcceptsGuidIdAndMaximumMessageLength()
    {
        AuthenticateStudent();
        var id = Guid.NewGuid();
        var message = new string('m', 4000);

        using var response = await _client.PostAsJsonAsync("/api/students/createhelprequest", new { Id = id, Message = message });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(id, await response.Content.ReadFromJsonAsync<Guid>());
        Assert.Equal(message, Assert.Single(_requests.Saved).Message);
    }

    public static TheoryData<Guid, string?, string?, string> InvalidHelpRequestCommands => new()
    {
        { Guid.Empty, "student-123", "Please help", "Id" },
        { TestIds.From("request-123"), null, "Please help", "UserId" },
        { TestIds.From("request-123"), "", "Please help", "UserId" },
        { TestIds.From("request-123"), " ", "Please help", "UserId" },
        { TestIds.From("request-123"), "student-123", null, "Message" },
        { TestIds.From("request-123"), "student-123", "", "Message" },
        { TestIds.From("request-123"), "student-123", " ", "Message" },
        { TestIds.From("request-123"), "student-123", new string('m', 4001), "Message" }
    };

    [Theory]
    [MemberData(nameof(InvalidHelpRequestCommands))]
    public async Task CommandBusRejectsInvalidHelpRequestsWithoutHttpOrSideEffects(
        Guid id, string? userId, string? message, string property)
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            new CreateHelpRequestCommand(id, userId!, message!).ExecuteAsync(CancellationToken.None));

        Assert.Equal(property, Assert.Single(exception.Errors).PropertyName, ignoreCase: true);
        Assert.Empty(_requests.Saved);
        Assert.Empty(_requests.RequestMessages);
        Assert.Empty(_requests.Notifications);
        Assert.Empty(_executionOrder);
    }

    [Fact]
    public async Task CommandBusCreatesHelpRequestWithoutHttp()
    {
        var result = await new CreateHelpRequestCommand(TestIds.From("request-123"), "student-123", "Please help")
            .ExecuteAsync(CancellationToken.None);

        Assert.Equal(TestIds.From("request-123"), result);
        Assert.Equal("student-123", Assert.Single(_requests.Saved).UserId);
        Assert.Equal((TestIds.From("request-123"), "Please help"), Assert.Single(_requests.RequestMessages));
        Assert.Equal((TestIds.From("request-123"), "student-123", "Please help"), Assert.Single(_requests.Notifications));
        Assert.Equal("Executing command CreateHelpRequestCommand", _executionOrder[0]);
        Assert.Equal("Executed command CreateHelpRequestCommand", _executionOrder[^1]);
    }

    [Fact]
    public async Task CancelledHelpRequestStopsBeforeLoggingAndSideEffects()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CreateHelpRequestCommand(TestIds.From("request-123"), "student-123", "Please help").ExecuteAsync(cancellation.Token));

        Assert.Empty(_requests.Saved);
        Assert.Empty(_requests.RequestMessages);
        Assert.Empty(_requests.Notifications);
        Assert.Empty(_executionOrder);
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
    public async Task HelpRequestHistoryUsesCookieIdentityAndPreservesArrayResponse()
    {
        AuthenticateStudent();
        using var identityResponse = await _client.GetAsync("/api/students/me");
        var identity = await identityResponse.Content.ReadFromJsonAsync<StudentIdentityResponse>();
        Assert.NotNull(identity);
        Assert.False(identityResponse.Headers.Contains("Set-Cookie"));
        _requests.Saved.AddRange(
        [
            new HelpRequest(TestIds.From("request-2"), identity.UserId, "Second request"),
            new HelpRequest(TestIds.From("request-1"), identity.UserId, "First request"),
            new HelpRequest(TestIds.From("other-request"), "other-student", "Private request"),
            new HelpRequest(TestIds.From("legacy-request"), null!, "Legacy request")
        ]);

        using var response = await _client.GetAsync("/api/students/requests?userId=other-student");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var requests = await response.Content.ReadFromJsonAsync<HelpRequestResult[]>();
        Assert.NotNull(requests);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"id\":", body);
        Assert.Contains("\"userId\":", body);
        Assert.Contains("\"message\":", body);
        Assert.Equal(new[] { TestIds.From("request-1"), TestIds.From("request-2") }.OrderBy(id => id), requests.Select(request => request.Id));
        Assert.All(requests, request => Assert.Equal(identity.UserId, request.UserId));
        Assert.Equal(new[] { "First request", "Second request" }, requests.Select(request => request.Message).OrderBy(message => message));
        Assert.Equal(identity.UserId, Assert.Single(_requests.Reads).UserId);
        Assert.Equal(new[]
        {
            "Executing command GetHelpRequestsQuery",
            $"Read help requests for {identity.UserId}",
            "Executed command GetHelpRequestsQuery"
        }, _executionOrder);
        Assert.Empty(_requests.RequestMessages);
        Assert.Empty(_requests.Notifications);
        Assert.Equal(4, _requests.Saved.Count);
    }

    [Fact]
    public async Task HelpRequestHistoryReturnsEmptyArrayForNewStudent()
    {
        AuthenticateStudent();
        using var response = await _client.GetAsync("/api/students/requests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
        Assert.Single(_requests.Reads);
    }

    [Fact]
    public async Task QueryUsesCommandBusOutsideHttpAndForwardsCancellationToken()
    {
        _requests.Saved.Add(new HelpRequest(TestIds.From("request-1"), "student-123", "Please help"));
        using var cancellation = new CancellationTokenSource();

        var results = await new GetHelpRequestsQuery("student-123").ExecuteAsync(cancellation.Token);

        Assert.Equal(new HelpRequestResult(TestIds.From("request-1"), "student-123", "Please help"), Assert.Single(results));
        var read = Assert.Single(_requests.Reads);
        Assert.Equal("student-123", read.UserId);
        Assert.Equal(cancellation.Token, read.CancellationToken);
        Assert.Equal(new[]
        {
            "Executing command GetHelpRequestsQuery",
            "Read help requests for student-123",
            "Executed command GetHelpRequestsQuery"
        }, _executionOrder);
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

    private sealed class RecordingRequestServices(List<string> executionOrder)
        : IHelpRequestRepository
    {
        public List<HelpRequest> Saved { get; } = [];
        public List<(string UserId, CancellationToken CancellationToken)> Reads { get; } = [];
        public List<(Guid Id, string Message)> RequestMessages { get; } = [];
        public List<(Guid Id, string? UserId, string Message)> Notifications { get; } = [];

        public Task AddAsync(HelpRequest request, IReadOnlyCollection<OutgoingMessage> messages, CancellationToken cancellationToken)
        {
            Saved.Add(request);
            executionOrder.Add($"Saved help request {request.Id}");
            foreach (var outgoing in messages)
            {
                if (outgoing.Payload is HelpRequestCreatedMessage help)
                {
                    RequestMessages.Add((outgoing.Id, help.Message));
                    executionOrder.Add($"Queued help request {outgoing.Id}");
                }
                else if (outgoing.Payload is NotificationCreatedMessage notification)
                {
                    Notifications.Add((outgoing.Id, notification.UserId, notification.Messages));
                    executionOrder.Add($"Queued notification {outgoing.Id}");
                }
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<HelpRequest>> GetByUserIdAsync(string userId, CancellationToken cancellationToken)
        {
            Reads.Add((userId, cancellationToken));
            executionOrder.Add($"Read help requests for {userId}");
            return Task.FromResult<IReadOnlyList<HelpRequest>>(
                Saved.Where(request => request.UserId == userId).OrderBy(request => request.Id).ToArray());
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
