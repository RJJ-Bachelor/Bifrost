using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using HeimdallGateway.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Shared.Api.Authentication;
using Yarp.ReverseProxy.Forwarder;

namespace HeimdallGateway.Test;

public sealed class StudentAuthenticationTests : IAsyncLifetime
{
    private readonly string keyRingPath = Path.Combine(Path.GetTempPath(), "bifrost-student-cookie-test-" + Guid.NewGuid().ToString("N"));
    private readonly SymmetricSecurityKey teacherKey = new(RandomNumberGenerator.GetBytes(32));
    private readonly Dictionary<string, WebApplication> services = [];
    private WebApplication gateway = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        services["eir"] = await StartServiceAsync("eir");
        services["gna"] = await StartServiceAsync("gna");
        services["mimir"] = await StartServiceAsync("mimir");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "gateway-appsettings.json"))
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "gateway-appsettings.Development.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:StudentCookie:KeyRingPath"] = keyRingPath,
                // Mimir currently has no student API. Model its future route with a protected test service.
                ["ReverseProxy:Routes:mimir-test:ClusterId"] = "mimir",
                ["ReverseProxy:Routes:mimir-test:Match:Path"] = "/api/students/mimir/{**catch-all}",
                ["ReverseProxy:Routes:mimir-test:AuthorizationPolicy"] = "Student",
                ["ReverseProxy:Clusters:mimir:Destinations:mimir-api:Address"] = "http://mimir/"
            });
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddHeimdallAuthentication(builder.Configuration, builder.Environment);
        ConfigureStudentProtection(builder.Services);
        ConfigureTeacherValidation(builder.Services);
        builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            options.Configuration = new OpenIdConnectConfiguration
            {
                Issuer = "https://test-identity",
                AuthorizationEndpoint = "https://test-identity/authorize"
            });
        builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
        builder.Services.AddSingleton<IForwarderHttpClientFactory>(new TestServiceClientFactory(services));
        gateway = builder.Build();
        gateway.UseHeimdallAuthentication();
        gateway.MapPost("/test/teacher-login", async (HttpContext context, bool expired) =>
        {
            var properties = new AuthenticationProperties();
            properties.StoreTokens([
                new AuthenticationToken { Name = "access_token", Value = TeacherToken() },
                new AuthenticationToken
                {
                    Name = "expires_at", Value = DateTimeOffset.UtcNow.AddMinutes(expired ? -1 : 10).ToString("O")
                }
            ]);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "teacher|opaque-id"), new Claim("role", "Teacher")],
                    CookieAuthenticationDefaults.AuthenticationScheme, "sub", "role")), properties);
        }).AllowAnonymous();
        gateway.MapReverseProxy();
        await gateway.StartAsync();
        client = gateway.GetTestClient();
    }

    [Fact]
    public async Task FirstStudentCallReachesAnotherServiceWithCookieAlreadyAttached()
    {
        using var response = await client.GetAsync("/api/students/mimir/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var identity = (await response.Content.ReadFromJsonAsync<ReceivedIdentity>())!;
        Assert.Equal("mimir", identity.Service);
        Assert.True(Guid.TryParseExact(identity.UserId, "N", out _));
        Assert.Equal(new[] { "Student" }, identity.Roles);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("path=/api/students", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=lax", cookie);
        Assert.Contains("max-age=31536000", cookie);
        Assert.Equal(StudentCookieHandler.CookieName + "=" + identity.Cookie, cookie.Split(';')[0]);
    }

    [Fact]
    public async Task StudentIdentityIsReusedAcrossServicesAndAfterServiceRestart()
    {
        using var first = await client.GetAsync("/api/students/mimir/me");
        var identity = (await first.Content.ReadFromJsonAsync<ReceivedIdentity>())!;
        client.DefaultRequestHeaders.Add("Cookie", StudentCookie(first));
        using var eir = await client.GetAsync("/api/students/me");
        Assert.Equal(identity.UserId, (await eir.Content.ReadFromJsonAsync<ReceivedIdentity>())!.UserId);
        Assert.False(eir.Headers.Contains("Set-Cookie"));

        await services["mimir"].DisposeAsync();
        services["mimir"] = await StartServiceAsync("mimir");
        using var restarted = services["mimir"].GetTestClient();
        restarted.DefaultRequestHeaders.Add("Cookie", StudentCookie(first));
        using var next = await restarted.GetAsync("/api/students/mimir/me");
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Equal(identity.UserId, (await next.Content.ReadFromJsonAsync<ReceivedIdentity>())!.UserId);
        Assert.False(next.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("eir")]
    [InlineData("mimir")]
    public async Task ServiceRejectsMissingCookieAndSpoofedIdentityHeaders(string service)
    {
        using var direct = services[service].GetTestClient();
        direct.DefaultRequestHeaders.Add("X-Bifrost-User-Id", "spoofed-student");
        direct.DefaultRequestHeaders.Add("X-Bifrost-Roles", "Student");
        using var response = await direct.GetAsync("/api/students/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("tampered")]
    [InlineData("different-purpose")]
    [InlineData("different-key-ring")]
    [InlineData("non-guid")]
    public async Task ServiceRejectsInvalidCookieAndGatewayReplacesIt(string kind)
    {
        var provider = gateway.Services.GetRequiredService<IOptionsMonitor<StudentCookieOptions>>()
            .Get(StudentCookieHandler.SchemeName).DataProtectionProvider!;
        var userId = Guid.NewGuid().ToString("N");
        var protector = provider.CreateProtector(StudentIdentityCookie.ProtectionPurpose).ToTimeLimitedDataProtector();
        var cookie = kind switch
        {
            "malformed" => "altered-cookie",
            "expired" => protector.Protect(userId, DateTimeOffset.UtcNow.AddMinutes(-1)),
            "different-purpose" => provider.CreateProtector("AnotherPurpose").ToTimeLimitedDataProtector().Protect(userId, TimeSpan.FromDays(1)),
            "different-key-ring" => new EphemeralDataProtectionProvider().CreateProtector(StudentIdentityCookie.ProtectionPurpose)
                .ToTimeLimitedDataProtector().Protect(userId, TimeSpan.FromDays(1)),
            "non-guid" => protector.Protect("student-id-without-guid", TimeSpan.FromDays(1)),
            _ => protector.Protect(userId, TimeSpan.FromDays(1))
        };
        if (kind == "tampered")
        {
            var middle = cookie.Length / 2;
            cookie = cookie[..middle] + (cookie[middle] == 'A' ? 'B' : 'A') + cookie[(middle + 1)..];
        }

        using var direct = services["mimir"].GetTestClient();
        direct.DefaultRequestHeaders.Add("Cookie", StudentCookieHandler.CookieName + "=" + cookie);
        using var rejected = await direct.GetAsync("/api/students/me");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.False(rejected.Headers.Contains("Set-Cookie"));

        client.DefaultRequestHeaders.Add("Cookie", StudentCookieHandler.CookieName + "=" + cookie);
        using var replaced = await client.GetAsync("/api/students/mimir/me");
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        var newIdentity = (await replaced.Content.ReadFromJsonAsync<ReceivedIdentity>())!;
        Assert.NotEqual(userId, newIdentity.UserId);
        Assert.NotEqual(cookie, newIdentity.Cookie);
        Assert.Equal(StudentCookieHandler.CookieName + "=" + newIdentity.Cookie, StudentCookie(replaced));
    }

    [Fact]
    public async Task GatewayRemovesSpoofedIdentityHeadersAndPreservesOtherCookies()
    {
        client.DefaultRequestHeaders.Add("Cookie", "another-cookie=kept; quoted-cookie=\"quoted-value\"");
        client.DefaultRequestHeaders.Add("X-Bifrost-User-Id", "spoofed-student");
        client.DefaultRequestHeaders.Add("X-Bifrost-Roles", "Teacher");
        using var response = await client.GetAsync("/api/students/mimir/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var identity = (await response.Content.ReadFromJsonAsync<ReceivedIdentity>())!;
        Assert.Equal("kept", identity.OtherCookie);
        Assert.Null(identity.SpoofedUserId);
        Assert.Null(identity.SpoofedRoles);
        Assert.NotEqual("spoofed-student", identity.UserId);
        Assert.Equal(new[] { "Student" }, identity.Roles);
    }

    [Fact]
    public async Task HttpsStudentCookieHasSecureFlag()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://gateway/api/students/me");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("secure", Assert.Single(response.Headers.GetValues("Set-Cookie")));
    }

    [Fact]
    public async Task StudentCookieDoesNotGrantTeacherAccess()
    {
        using var first = await client.GetAsync("/api/students/me");
        client.DefaultRequestHeaders.Add("Cookie", StudentCookie(first));
        using var response = await client.GetAsync("/api/teachers/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            cookie => cookie.StartsWith(StudentCookieHandler.CookieName + "="));
    }

    [Theory]
    [InlineData("Bifrost", HttpStatusCode.OK)]
    [InlineData("Other", HttpStatusCode.Forbidden)]
    public async Task TeacherJwtIsForwardedAndServiceChecksItsScope(string scope, HttpStatusCode expected)
    {
        var token = TeacherToken(scope);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync("/api/teachers/me");
        Assert.Equal(expected, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        if (expected == HttpStatusCode.OK)
        {
            var identity = (await response.Content.ReadFromJsonAsync<ReceivedIdentity>())!;
            Assert.Equal("teacher|opaque-id", identity.UserId);
            Assert.Equal(new[] { "Teacher" }, identity.Roles);
            Assert.Equal("Bearer " + token, identity.Authorization);
        }
    }

    [Theory]
    [InlineData(false, HttpStatusCode.OK)]
    [InlineData(true, HttpStatusCode.Unauthorized)]
    public async Task TeacherCookieStillForwardsValidAccessToken(bool expired, HttpStatusCode expected)
    {
        using var login = await client.PostAsync("/test/teacher-login?expired=" + expired.ToString().ToLowerInvariant(), null);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';')[0];
        Assert.StartsWith("bifrost-teacher=", cookie);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        using var response = await client.GetAsync("/api/teachers/me");
        Assert.Equal(expected, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        if (expected == HttpStatusCode.OK)
            Assert.Equal("teacher|opaque-id", (await response.Content.ReadFromJsonAsync<ReceivedIdentity>())!.UserId);
    }

    [Theory]
    [InlineData("/api/teachers/development/seed", "eir", "/api/test/seed", false, HttpStatusCode.OK)]
    [InlineData("/api/teachers/development/generalizations", "mimir", "/api/test/generalizations", false, HttpStatusCode.OK)]
    [InlineData("/api/teachers/development/generalizations/from-help-requests", "mimir", "/api/test/generalizations/from-help-requests", false, HttpStatusCode.OK)]
    [InlineData("/api/teachers/development/seed", "eir", "/api/test/seed", true, HttpStatusCode.Unauthorized)]
    [InlineData("/api/teachers/development/generalizations", "mimir", "/api/test/generalizations", true, HttpStatusCode.Unauthorized)]
    [InlineData("/api/teachers/development/generalizations/from-help-requests", "mimir", "/api/test/generalizations/from-help-requests", true, HttpStatusCode.Unauthorized)]
    public async Task DevelopmentCallsUseExistingTeacherCookie(string route, string service, string targetPath,
        bool expired, HttpStatusCode expected)
    {
        using var login = await client.PostAsync("/test/teacher-login?expired=" + expired.ToString().ToLowerInvariant(), null);
        client.DefaultRequestHeaders.Add("Cookie", Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';')[0]);
        using var response = await client.PostAsJsonAsync(route, new { scenario = "faq-demo", studentCount = 2 });
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var identity = (await response.Content.ReadFromJsonAsync<ReceivedIdentity>())!;
            Assert.Equal(service, identity.Service);
            Assert.Equal(targetPath, identity.Path);
            Assert.Equal("teacher|opaque-id", identity.UserId);
            Assert.StartsWith("Bearer ", identity.Authorization);
        }
    }

    [Theory]
    [InlineData("/api/teachers/development/seed", false)]
    [InlineData("/api/teachers/development/generalizations", false)]
    [InlineData("/api/teachers/development/generalizations/from-help-requests", false)]
    [InlineData("/api/teachers/development/seed", true)]
    [InlineData("/api/teachers/development/generalizations", true)]
    [InlineData("/api/teachers/development/generalizations/from-help-requests", true)]
    public async Task DevelopmentCallsRequireTeacherLogin(string route, bool student)
    {
        if (student)
        {
            using var first = await client.GetAsync("/api/students/me");
            client.DefaultRequestHeaders.Add("Cookie", StudentCookie(first));
        }
        using var response = await client.PostAsJsonAsync(route, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MonitoringDoesNotIssueStudentCookie()
    {
        using var response = await client.GetAsync("/api/monitoring/alive");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task StudentRoutesUseStudentCookieWhileTeacherIsLoggedIn()
    {
        using var first = await client.GetAsync("/api/students/me");
        var identity = (await first.Content.ReadFromJsonAsync<ReceivedIdentity>())!;
        client.DefaultRequestHeaders.Add("Cookie", StudentCookie(first));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TeacherToken());
        using var queue = await client.GetAsync("/api/students/sessions/11111111-1111-1111-1111-111111111111/queue");
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
        var actor = (await queue.Content.ReadFromJsonAsync<ReceivedIdentity>())!;
        Assert.Equal(identity.UserId, actor.UserId);
        Assert.Equal(new[] { "Student" }, actor.Roles);
    }

    [Fact]
    public async Task TeacherRoutesUseTeacherJwtWhenStudentCookieAlsoExists()
    {
        using var first = await client.GetAsync("/api/students/me");
        client.DefaultRequestHeaders.Add("Cookie", StudentCookie(first));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TeacherToken());
        using var request = await client.GetAsync("/api/teachers/requests/11111111-1111-1111-1111-111111111111");
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        var actor = (await request.Content.ReadFromJsonAsync<ReceivedIdentity>())!;
        Assert.Equal("teacher|opaque-id", actor.UserId);
        Assert.Equal(new[] { "Teacher" }, actor.Roles);
    }

    [Theory]
    [InlineData("Bifrost", HttpStatusCode.OK)]
    [InlineData("Other", HttpStatusCode.Forbidden)]
    public async Task TeacherAccessRequiresScopeEvenWithStudentRole(string scope, HttpStatusCode expected)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TeacherToken(scope, includeStudentRole: true));
        using var response = await client.GetAsync("/api/teachers/sessions/11111111-1111-1111-1111-111111111111/queue");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task OldSharedHelpRequestRoutesAreNotPublished()
    {
        using var response = await client.GetAsync("/api/requests/11111111-1111-1111-1111-111111111111");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task NotificationRouteForwardsStudentCookieToGna()
    {
        using var response = await client.GetAsync("/api/students/notifications/negotiate");
        var identity = (await response.Content.ReadFromJsonAsync<ReceivedIdentity>())!;
        Assert.Equal("gna", identity.Service);
        Assert.Contains("Student", identity.Roles);
        Assert.False(string.IsNullOrWhiteSpace(identity.Cookie));
    }

    [Fact]
    public async Task NotificationRouteForwardsTeacherJwtFromQuery()
    {
        using var response = await client.GetAsync("/api/teachers/notifications?access_token=" + TeacherToken());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var identity = (await response.Content.ReadFromJsonAsync<ReceivedIdentity>())!;
        Assert.Equal("gna", identity.Service);
        Assert.Equal("teacher|opaque-id", identity.UserId);
        Assert.StartsWith("Bearer ", identity.Authorization);
    }

    [Fact]
    public async Task TeacherNotificationQueryRejectsInvalidToken()
    {
        using var response = await client.GetAsync("/api/teachers/notifications?access_token=invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<WebApplication> StartServiceAsync(string name)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration["Authentication:StudentCookie:KeyRingPath"] = keyRingPath;
        builder.Services.AddBifrostServiceAuthentication(builder.Configuration, builder.Environment);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        ConfigureStudentProtection(builder.Services);
        ConfigureTeacherValidation(builder.Services);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        ReceivedIdentity Identity(HttpContext context) => new(name, context.User.FindFirst("sub")!.Value,
            context.User.FindAll("role").Select(claim => claim.Value).ToArray(),
            context.Request.Cookies[StudentCookieHandler.CookieName], context.Request.Cookies["another-cookie"],
            context.Request.Headers["X-Bifrost-User-Id"].FirstOrDefault(), context.Request.Headers["X-Bifrost-Roles"].FirstOrDefault(),
            context.Request.Headers.Authorization.ToString(), context.Request.Path.ToString());
        app.MapGet("/api/students/{**path}", Identity).RequireAuthorization("Student");
        app.MapGet("/api/teachers/{**path}", Identity).RequireAuthorization("Teacher");
        if (name == "eir") app.MapPost("/api/test/seed", Identity).RequireAuthorization("Teacher");
        if (name == "mimir")
        {
            app.MapPost("/api/test/generalizations", Identity).RequireAuthorization("Teacher");
            app.MapPost("/api/test/generalizations/from-help-requests", Identity).RequireAuthorization("Teacher");
        }
        app.MapGet("/api/monitoring/alive", () => "Alive").AllowAnonymous();
        await app.StartAsync();
        return app;
    }

    // Test keys are isolated and do not depend on the Windows user's DPAPI profile.
    private void ConfigureStudentProtection(IServiceCollection services) =>
        services.PostConfigure<StudentCookieOptions>(StudentCookieHandler.SchemeName, options =>
            options.DataProtectionProvider = DataProtectionProvider.Create(new DirectoryInfo(keyRingPath),
                protection => protection.SetApplicationName("Bifrost.StudentIdentity")));

    private void ConfigureTeacherValidation(IServiceCollection services) =>
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = null;
            options.TokenValidationParameters.ValidIssuer = "https://test-identity";
            options.TokenValidationParameters.IssuerSigningKey = teacherKey;
        });

    private string TeacherToken(string scope = "Bifrost", bool includeStudentRole = false) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        issuer: "https://test-identity", audience: "Bifrost",
        claims: new[] { new Claim("sub", "teacher|opaque-id"), new Claim("role", "Teacher"), new Claim("scope", scope) }
            .Concat(includeStudentRole ? [new Claim("role", "Student")] : []),
        expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(teacherKey, SecurityAlgorithms.HmacSha256)));

    private static string StudentCookie(HttpResponseMessage response) => Assert.Single(response.Headers.GetValues("Set-Cookie"),
        value => value.StartsWith(StudentCookieHandler.CookieName + "=") && value.Contains("max-age=31536000")).Split(';')[0];

    public async Task DisposeAsync()
    {
        client.Dispose();
        await gateway.DisposeAsync();
        foreach (var service in services.Values) await service.DisposeAsync();
        if (Directory.Exists(keyRingPath)) Directory.Delete(keyRingPath, recursive: true);
    }

    private sealed record ReceivedIdentity(string Service, string UserId, string[] Roles, string? Cookie,
        string? OtherCookie, string? SpoofedUserId, string? SpoofedRoles, string? Authorization, string Path);

    private sealed class TestServiceClientFactory(Dictionary<string, WebApplication> services) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) =>
            new(services[context.ClusterId].GetTestServer().CreateHandler());
    }
}
