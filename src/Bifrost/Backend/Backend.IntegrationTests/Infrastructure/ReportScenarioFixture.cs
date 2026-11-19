using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.Json;
using EirService.Api.Middleware.Commands;
using EirService.Api.Middleware;
using EirService.HelpRequests.Application.Features.Student.CreateHelpRequest;
using EirService.HelpRequests.Application;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.HelpRequests.Infrastructure;
using EirService.Insights.Application.Features.Teacher.GetSessionStatistics;
using EirService.Insights.Application;
using EirService.KnowledgeBase.Application.Features.Common.GetSessionFaqs;
using EirService.KnowledgeBase.Application;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using EirService.KnowledgeBase.Infrastructure;
using EirService.Participants.Application.Features.Student.CreateIdentityTransferCode;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Application.Features.Student.RedeemIdentityTransferCode;
using EirService.Participants.Application.Features.Teacher.JoinSession;
using EirService.Participants.Infrastructure.Persistence;
using EirService.Participants.Infrastructure;
using EirService.Sessions.Application.Features.Common.GetSessionArchive;
using EirService.Sessions.Application.Features.Common.GetSessionArchives;
using EirService.Sessions.Application.Features.Student.GetStudentSession;
using EirService.Sessions.Application.Features.Teacher.ArchiveSession;
using EirService.Sessions.Application.Features.Teacher.ClearSession;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using EirService.Sessions.Application.Features.Teacher.DeleteSession;
using EirService.Sessions.Application.Features.Teacher.GetSession;
using EirService.Sessions.Application.Features.Teacher.GetSessions;
using EirService.Sessions.Application.Features.Teacher.UpdateSession;
using EirService.Sessions.Infrastructure.Persistence;
using EirService.Sessions.Infrastructure;
using FastEndpoints;
using FluentValidation;
using GnaService.Api.Hubs;
using GnaService.Application.Notifications;
using GnaService.Application.Services.Messaging;
using GnaService.Infrastructure.Persistence;
using GnaService.Infrastructure;
using HeimdallGateway.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimirService.Application.Services.Llm;
using MimirService.Infrastructure.Persistence;
using MimirService.Infrastructure;
using RabbitMQ.Client;
using Shared.Api.Authentication;
using Shared.Application.Messaging;
using Shared.Infrastructure.Persistence.Migrations;
using Shared.Testing;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Real HTTP, AMQP, PostgreSQL and SignalR with production registrations. Backend modules
/// share one host because FastEndpoints keeps process-global registries; Heimdall has its own host.
/// This tests component integration, not independent process deployment or the identity provider.
/// </summary>
internal sealed class ReportScenarioFixture : IAsyncDisposable
{
    private readonly PostgresTestDatabase eir = new("report_eir");
    private readonly PostgresTestDatabase gna = new("report_gna");
    private readonly PostgresTestDatabase mimir = new("report_mimir");
    private readonly TestAuthentication authentication = new();
    private readonly CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));
    private readonly Dictionary<HttpClient, CookieContainer> cookies = [];
    private readonly DeterministicLlm llm = new();
    private readonly bool withMimir;
    private RabbitMqTestBroker? broker;
    private TcpFaultProxy? proxy;
    private WebApplication? backend;
    private WebApplication? gateway;
    private Uri? backendAddress;
    private string rabbitConnection = "";

    private ReportScenarioFixture(bool withMimir) => this.withMimir = withMimir;
    public IServiceProvider Services => backend!.Services;
    public CancellationToken Token => deadline.Token;
    public Uri GatewayAddress { get; private set; } = null!;
    public int LlmCalls => Volatile.Read(ref llm.Calls);
    public string GeneralizedQuestion => DeterministicLlm.Question;

    public static async Task<ReportScenarioFixture> CreateAsync(bool withMimir = false)
    {
        var fixture = new ReportScenarioFixture(withMimir);
        try
        {
            await fixture.InitializeAsync();
            return fixture;
        }
        catch (Exception startup)
        {
            try { await fixture.DisposeAsync(); }
            catch (Exception cleanup) { throw new AggregateException("Scenario startup and cleanup failed.", startup, cleanup); }
            throw;
        }
    }

    private async Task InitializeAsync()
    {
        var postgres = Environment.GetEnvironmentVariable("BIFROST_TEST_POSTGRES")!;
        await eir.InitializeAsync(postgres, Token);
        await gna.InitializeAsync(postgres, Token);
        await mimir.InitializeAsync(postgres, Token);
        broker = await RabbitMqTestBroker.CreateAsync(Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ")!,
            Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ_MANAGEMENT")!, Token);
        proxy = await TcpFaultProxy.StartAsync(new Uri(broker.ConnectionString), Token);
        rabbitConnection = new UriBuilder(broker.ConnectionString) { Host = "127.0.0.1", Port = proxy.Port }.Uri.ToString();
        await StartBackendAsync();
        await StartGatewayAsync();
    }

    private async Task StartBackendAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(backendAddress?.ToString() ?? "http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:eir"] = eir.ConnectionString,
            ["ConnectionStrings:gna"] = gna.ConnectionString,
            ["ConnectionStrings:mimir"] = mimir.ConnectionString,
            ["ConnectionStrings:rabbitmq"] = rabbitConnection,
            ["Generalization:Decider"] = withMimir ? "Gemini" : "Fake"
        });
        builder.Services.AddHelpRequestsInfrastructure(builder.Configuration);
        builder.Services.AddHelpRequestsApplication();
        builder.Services.AddInsightsApplication();
        builder.Services.AddSessionsInfrastructure(builder.Configuration);
        builder.Services.AddParticipantsInfrastructure(builder.Configuration);
        builder.Services.AddKnowledgeBaseInfrastructure(builder.Configuration);
        builder.Services.AddKnowledgeBaseApplication();
        builder.Services.AddGnaInfrastructure(builder.Configuration);
        builder.Services.AddNotificationRealtime();
        builder.Services.AddMimirInfrastructure(builder.Configuration);
        builder.Services.RemoveAll<ILlmClient>();
        builder.Services.AddSingleton<ILlmClient>(llm);
        authentication.Register(builder);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(EirService.Api.Program).Assembly,
                typeof(CreateSessionCommandHandler).Assembly, typeof(CreateHelpRequestCommandHandler).Assembly,
                typeof(JoinSessionCommandHandler).Assembly, typeof(GetSessionFaqsQueryHandler).Assembly,
                typeof(GetSessionStatisticsQueryHandler).Assembly, typeof(NotificationMessageHandler).Assembly];
        });
        builder.Services.AddTransient<IValidator<CreateSessionCommand>, CreateSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<UpdateSessionCommand>, UpdateSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<ClearSessionCommand>, ClearSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<ArchiveSessionCommand>, ArchiveSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<GetSessionArchivesQuery>, GetSessionArchivesQueryValidator>();
        builder.Services.AddTransient<IValidator<GetSessionArchiveQuery>, GetSessionArchiveQueryValidator>();
        builder.Services.AddTransient<IValidator<DeleteSessionCommand>, DeleteSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<GetSessionsQuery>, GetSessionsQueryValidator>();
        builder.Services.AddTransient<IValidator<GetSessionQuery>, GetSessionQueryValidator>();
        builder.Services.AddTransient<IValidator<GetStudentSessionQuery>, GetStudentSessionQueryValidator>();
        builder.Services.AddTransient<IValidator<JoinSessionCommand>, JoinSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<CreateIdentityTransferCodeCommand>, CreateIdentityTransferCodeCommandValidator>();
        builder.Services.AddTransient<IValidator<RedeemIdentityTransferCodeCommand>, RedeemIdentityTransferCodeCommandValidator>();
        builder.Services.AddTransient<IValidator<JoinTeacherSessionCommand>, JoinTeacherSessionCommandValidator>();
        builder.Services.AddCommandMiddleware(options => options.Register(typeof(CommandValidationMiddleware<,>),
            typeof(CommandLoggingMiddleware<,>)));
        backend = builder.Build();
        await using (var scope = backend.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Database.MigrateAtStartupAsync(Token);
            await scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Database.MigrateAtStartupAsync(Token);
            await scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>().Database.MigrateAtStartupAsync(Token);
            await scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Database.MigrateAtStartupAsync(Token);
            await scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Database.MigrateAtStartupAsync(Token);
            await scope.ServiceProvider.GetRequiredService<MimirRequestDbContext>().Database.MigrateAtStartupAsync(Token);
        }
        backend.UseMiddleware<ValidationExceptionMiddleware>();
        backend.UseAuthentication();
        backend.UseAuthorization();
        backend.UseFastEndpoints(configuration =>
        {
            configuration.Endpoints.RoutePrefix = "api";
            configuration.Serializer.Options.Converters.Add(new JsonStringEnumConverter());
        });
        backend.MapNotificationHubs();
        await backend.StartAsync(Token);
        backendAddress = new Uri(backend.Urls.Single());
        // The AMQP server, rather than BackgroundService.StartAsync, establishes subscriber readiness.
        await WaitForConsumersAsync();
    }

    private async Task WaitForConsumersAsync()
    {
        await using var connection = await new ConnectionFactory { Uri = new Uri(broker!.ConnectionString) }.CreateConnectionAsync(Token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Token);
        foreach (var queue in new[] { "mimir-request-created", "eir-generalized-created", MessageRoutes.NotificationCreated, MessageRoutes.SessionChanged })
        {
            // Declaring the durable queue here is safe; production subscribers own bindings and consumption.
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: Token);
            await WaitUntilAsync(async () => await channel.ConsumerCountAsync(queue, Token) > 0, Token);
        }
    }

    private async Task StartGatewayAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "report-gateway-appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:eir:Destinations:eir-api:Address"] = backendAddress!.ToString(),
                ["ReverseProxy:Clusters:gna:Destinations:gna-api:Address"] = backendAddress.ToString()
            });
        var studentProtection = Services.GetRequiredService<IOptionsMonitor<StudentCookieOptions>>()
            .Get(StudentCookieHandler.SchemeName).DataProtectionProvider;
        builder.Services.Configure<StudentCookieOptions>(StudentCookieHandler.SchemeName,
            options => options.DataProtectionProvider = studentProtection);
        builder.Services.AddHeimdallAuthentication(builder.Configuration, builder.Environment);
        var validation = Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = null;
            options.TokenValidationParameters = validation.Clone();
        });
        builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
        gateway = builder.Build();
        gateway.UseHeimdallAuthentication();
        gateway.MapReverseProxy();
        await gateway.StartAsync(Token);
        GatewayAddress = new Uri(gateway.Urls.Single());
    }

    public string TeacherToken(string subject = "teacher", string role = "Teacher", string scope = "Bifrost", DateTime? expires = null)
        => authentication.Token(subject, role, scope, expires: expires);

    public HttpClient Teacher(string subject = "teacher", string role = "Teacher", string scope = "Bifrost", string? tokenOverride = null)
    {
        var client = Student();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenOverride ?? TeacherToken(subject, role, scope));
        return client;
    }

    public HttpClient Student()
    {
        var jar = new CookieContainer();
        var client = new HttpClient(new HttpClientHandler { CookieContainer = jar, AllowAutoRedirect = false })
        {
            BaseAddress = GatewayAddress, Timeout = TimeSpan.FromSeconds(30)
        };
        cookies.Add(client, jar);
        return client;
    }

    public async Task<WebSocket> ConnectStudentAsync(HttpClient student, CancellationToken token)
    {
        using var identity = await student.GetAsync("/api/students/me", token);
        identity.EnsureSuccessStatusCode();
        var user = (await identity.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("userId").GetString()!;
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.Cookies = cookies[student];
            await socket.ConnectAsync(new UriBuilder(GatewayAddress)
            {
                Scheme = "ws", Path = NotificationHubAuthentication.StudentPath
            }.Uri, token);
            await SignalRTestClient.HandshakeAsync(socket, token);
            await WaitUntilAsync(() => Task.FromResult(Services.GetRequiredService<IConnectionRegistry>().IsConnected(user)), token);
            return socket;
        }
        catch { socket.Dispose(); throw; }
    }

    public Task DisconnectBrokerAsync() => proxy!.DisconnectAsync();
    public void ResumeBroker() => proxy!.Resume();

    public async Task RestartBackendAsync()
    {
        if (backend is not null)
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await backend.StopAsync(shutdown.Token);
            await backend.DisposeAsync();
            backend = null;
        }
        // Consumers cannot reconnect until the caller resumes the fault proxy. StartBackendAsync
        // waits for consumers, so reconnect immediately after reconstructing the stopped backend.
        proxy!.Resume();
        await StartBackendAsync();
    }

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken token,
        [CallerArgumentExpression(nameof(condition))] string? description = null)
    {
        try
        {
            while (!await condition()) await Task.Delay(TimeSpan.FromMilliseconds(50), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw new TimeoutException($"Scenario did not reach the expected state: {description}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        async Task Clean(Func<Task> action)
        {
            try { await action(); }
            catch (Exception error) { failures.Add(error); }
        }
        foreach (var client in cookies.Keys) client.Dispose();
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        if (gateway is not null) await Clean(async () => { try { await gateway.StopAsync(shutdown.Token); } finally { await gateway.DisposeAsync(); } });
        if (backend is not null) await Clean(async () => { try { await backend.StopAsync(shutdown.Token); } finally { await backend.DisposeAsync(); } });
        if (proxy is not null) await Clean(() => proxy.DisposeAsync().AsTask());
        if (broker is not null) await Clean(() => broker.DisposeAsync().AsTask());
        await Clean(() => mimir.DisposeAsync().AsTask());
        await Clean(() => gna.DisposeAsync().AsTask());
        await Clean(() => eir.DisposeAsync().AsTask());
        deadline.Dispose();
        if (failures.Count > 0) throw new AggregateException("Scenario resources could not be cleaned up.", failures);
    }

    private sealed class DeterministicLlm : ILlmClient
    {
        public const string Question = "How can we solve the shared problem?";
        public int Calls;
        public Task<string> GenerateAsync(string systemInstruction, string prompt, JsonElement responseSchema, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Calls);
            return Task.FromResult(JsonSerializer.Serialize(new { decision = "pair", candidateId = "r1", faqQuestion = Question }));
        }
    }
}

internal sealed class ReportScenarioFactAttribute : FactAttribute
{
    public ReportScenarioFactAttribute()
    {
        if (new[] { "BIFROST_TEST_POSTGRES", "BIFROST_TEST_RABBITMQ", "BIFROST_TEST_RABBITMQ_MANAGEMENT" }
            .Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "Set BIFROST_TEST_POSTGRES, BIFROST_TEST_RABBITMQ and BIFROST_TEST_RABBITMQ_MANAGEMENT to isolated test servers.";
    }
}