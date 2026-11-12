using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.Api.Endpoints.Student.Participants.GetStudentIdentity;
using EirService.Api.Middleware;
using EirService.Api.Middleware.Commands;
using EirService.Participants.Application.Features.Student.CreateIdentityTransferCode;
using EirService.Participants.Application.Features.Student.RedeemIdentityTransferCode;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Api.Authentication;
using Shared.Domain.ValueObjects;

namespace EirService.Test;

[Collection("FastEndpoints")]
public sealed class IdentityTransferTests : IAsyncLifetime
{
    private const string CodesPath = "/api/students/identity-transfer-codes";
    private readonly TransferClock clock = new();
    private readonly MemoryTransferRepository transfers = new();
    private WebApplication app = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Host.UseDefaultServiceProvider(options => options.ValidateScopes = true);
        builder.WebHost.UseTestServer();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(Api.Program).Assembly, typeof(CreateIdentityTransferCodeCommand).Assembly];
            options.Filter = type => type.Namespace is { } name &&
                (name.EndsWith(".CreateIdentityTransferCode", StringComparison.Ordinal) ||
                 name.EndsWith(".RedeemIdentityTransferCode", StringComparison.Ordinal) ||
                 name.EndsWith(".GetStudentIdentity", StringComparison.Ordinal));
        });
        builder.Services.AddSingleton<IStudentIdentityTransferRepository>(transfers);
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddTransient<IValidator<CreateIdentityTransferCodeCommand>, CreateIdentityTransferCodeCommandValidator>();
        builder.Services.AddTransient<IValidator<RedeemIdentityTransferCodeCommand>, RedeemIdentityTransferCodeCommandValidator>();
        builder.Services.AddCommandMiddleware(options => options.Register(typeof(CommandValidationMiddleware<,>)));
        var dataProtection = new EphemeralDataProtectionProvider();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, CommandBusTests.TestAuthenticationHandler>(JwtBearerDefaults.AuthenticationScheme, _ => { })
            .AddStudentCookie(builder.Configuration, options => options.DataProtectionProvider = dataProtection);
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Student", policy => policy
                .AddAuthenticationSchemes(StudentCookieHandler.SchemeName)
                .RequireAuthenticatedUser().RequireClaim("sub").RequireRole("Student"));
        });
        builder.Services.RemoveAll<IDataProtectionProvider>();
        builder.Services.AddSingleton<IDataProtectionProvider>(dataProtection);
        builder.Services.RemoveAll<IHostedService>();
        app = builder.Build();
        app.UseMiddleware<ValidationExceptionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseFastEndpoints(configuration =>
        {
            configuration.Endpoints.RoutePrefix = "api";
            configuration.Serializer.Options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });
        await app.StartAsync();
    }

    [Fact]
    public async Task CreatesShortLivedCodeForTheCookieIdentityWithoutABody()
    {
        var studentId = Guid.NewGuid().ToString("N");
        using var source = StudentClient(studentId);

        var result = await CreateCodeAsync(source);

        Assert.True(ShortCode.IsValid(result.Code));
        Assert.Equal(clock.Now.AddMinutes(10), result.ExpiresAtUtc);
        Assert.Equal(studentId, Assert.Single(transfers.Created).StudentId);
    }

    [Fact]
    public async Task CreateIgnoresAnIdentitySuppliedInTheBody()
    {
        var studentId = Guid.NewGuid().ToString("N");
        using var source = StudentClient(studentId);
        using var response = await source.PostAsJsonAsync(CodesPath, new { StudentId = Guid.NewGuid().ToString("N") });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(studentId, Assert.Single(transfers.Created).StudentId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-cookie")]
    public async Task CreateRequiresAValidStudentCookie(string? cookie)
    {
        using var client = app.GetTestClient();
        if (cookie is not null) client.DefaultRequestHeaders.Add("Cookie", $"{StudentCookieHandler.CookieName}={cookie}");

        using var response = await client.PostAsync(CodesPath, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(transfers.Created);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RedeemIssuesAProtectedCookieForTheSourceIdentityAndReplacesAnyExistingIdentity(bool existingIdentity)
    {
        var studentId = Guid.NewGuid().ToString("N");
        using var source = StudentClient(studentId);
        var code = await CreateCodeAsync(source);
        using var target = existingIdentity ? StudentClient(Guid.NewGuid().ToString("N")) : app.GetTestClient();
        target.BaseAddress = new Uri("https://localhost");

        using var response = await target.PostAsJsonAsync(CodesPath + "/redeem", new
        {
            Code = " " + code.Code.ToLowerInvariant() + " ",
            StudentId = Guid.NewGuid().ToString("N")
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/students", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=31536000", cookie, StringComparison.OrdinalIgnoreCase);
        var cookieHeader = cookie.Split(';')[0];
        var protectedIdentity = cookieHeader[(cookieHeader.IndexOf('=') + 1)..];
        Assert.True(app.Services.GetRequiredService<StudentIdentityCookie>().TryUnprotect(protectedIdentity, out var restoredStudentId));
        Assert.Equal(studentId, restoredStudentId);
        target.DefaultRequestHeaders.Remove("Cookie");
        target.DefaultRequestHeaders.Add("Cookie", cookieHeader);
        var restored = await target.GetFromJsonAsync<StudentIdentityResponse>("/api/students/me");
        Assert.Equal(studentId, restored!.UserId);
        Assert.Contains("Student", restored.Roles);
    }

    [Fact]
    public async Task ACodeCanOnlyBeRedeemedOnce()
    {
        using var source = StudentClient(Guid.NewGuid().ToString("N"));
        var code = await CreateCodeAsync(source);
        using var target = app.GetTestClient();
        using var first = await target.PostAsJsonAsync(CodesPath + "/redeem", new { code.Code });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        using var second = await target.PostAsJsonAsync(CodesPath + "/redeem", new { code.Code });

        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.False(second.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("form-body")]
    [InlineData("form-query")]
    [InlineData("text-plain")]
    public async Task BrowserFormSubmissionsCannotChangeIdentityOrConsumeTheTransferCode(string submission)
    {
        using var source = StudentClient(Guid.NewGuid().ToString("N"));
        var code = await CreateCodeAsync(source);
        using var target = app.GetTestClient();
        target.DefaultRequestHeaders.Add("Origin", "https://another-site.example");
        var path = CodesPath + "/redeem" + (submission == "form-query" ? $"?code={code.Code}" : "");
        using HttpContent content = submission switch
        {
            "form-body" => new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = code.Code }),
            "form-query" => new FormUrlEncodedContent([]),
            _ => new StringContent(JsonSerializer.Serialize(new { code.Code }))
        };

        using var rejected = await target.PostAsync(path, content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, rejected.StatusCode);
        Assert.False(rejected.Headers.Contains("Set-Cookie"));
        target.DefaultRequestHeaders.Remove("Origin");
        using var valid = await target.PostAsJsonAsync(CodesPath + "/redeem", new { code.Code });
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
        Assert.Single(valid.Headers.GetValues("Set-Cookie"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("ABCD")]
    [InlineData("ABCD0123")]
    [InlineData("ABCDEFGHJ")]
    public async Task MalformedCodesReturnBadRequestWithoutIssuingACookie(string? code)
    {
        using var target = app.GetTestClient();

        using var response = await target.PostAsJsonAsync(CodesPath + "/redeem", new { Code = code });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task UnknownCodeDoesNotIssueACookie()
    {
        using var target = app.GetTestClient();

        using var response = await target.PostAsJsonAsync(CodesPath + "/redeem", new { Code = "ABCDEFGH" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task ACodeExpiresAtItsDeclaredExpiryTime()
    {
        using var source = StudentClient(Guid.NewGuid().ToString("N"));
        var code = await CreateCodeAsync(source);
        clock.Now = code.ExpiresAtUtc;
        using var target = app.GetTestClient();

        using var response = await target.PostAsJsonAsync(CodesPath + "/redeem", new { code.Code });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    private HttpClient StudentClient(string studentId)
    {
        var client = app.GetTestClient();
        var cookie = app.Services.GetRequiredService<StudentIdentityCookie>().Protect(studentId);
        client.DefaultRequestHeaders.Add("Cookie", $"{StudentCookieHandler.CookieName}={cookie}");
        return client;
    }

    private static async Task<CreateIdentityTransferCodeResult> CreateCodeAsync(HttpClient source)
    {
        using var response = await source.PostAsync(CodesPath, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateIdentityTransferCodeResult>())!;
    }

    public async Task DisposeAsync() => await app.DisposeAsync();

    private sealed class TransferClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MemoryTransferRepository : IStudentIdentityTransferRepository
    {
        private readonly Dictionary<string, StudentIdentityTransfer> codes = [];
        public List<StudentIdentityTransfer> Created { get; } = [];

        public Task<bool> TryAddAsync(StudentIdentityTransfer transfer, CancellationToken cancellationToken)
        {
            if (!codes.TryAdd(transfer.Code, transfer)) return Task.FromResult(false);
            Created.Add(transfer);
            return Task.FromResult(true);
        }

        public Task<string?> RedeemAsync(string code, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (!codes.TryGetValue(code, out var transfer) || transfer.ExpiresAtUtc <= now)
                return Task.FromResult<string?>(null);
            codes.Remove(code);
            return Task.FromResult<string?>(transfer.StudentId);
        }
    }
}
