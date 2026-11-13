using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using FastEndpoints;
using FluentValidation;
using GnaService.Api;
using GnaService.Api.Middleware;
using GnaService.Application.Features.Common.RegisterPushSubscription;
using GnaService.Application.Features.Common.UnregisterPushSubscription;
using GnaService.Application.Repositories;
using GnaService.Application.Services.Messaging;
using GnaService.Domain.Entities;
using GnaService.Infrastructure.Push;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shared.Api.Authentication;

namespace GnaService.Test;

[Collection("FastEndpoints")]
public sealed class PushSubscriptionApiTests : IAsyncLifetime
{
    private const string Student = "11111111111111111111111111111111";
    private const string Endpoint = "https://fcm.googleapis.com/fcm/send/device";
    private readonly SymmetricSecurityKey signingKey = new(RandomNumberGenerator.GetBytes(32));
    private readonly SubscriptionStore subscriptions = new();
    private readonly string publicKey = NewPublicKey();
    private readonly string auth = Base64Url(RandomNumberGenerator.GetBytes(16));
    private WebApplication app = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        var protection = new EphemeralDataProtectionProvider();
        builder.Services.AddSingleton<IDataProtectionProvider>(protection);
        builder.Services.AddBifrostServiceAuthentication(builder.Configuration, builder.Environment);
        builder.Services.PostConfigure<StudentCookieOptions>(StudentCookieHandler.SchemeName,
            options => options.DataProtectionProvider = protection);
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = null;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = "gna-push-tests", ValidateAudience = true, ValidAudience = "Bifrost",
                ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey, ValidateLifetime = true,
                NameClaimType = "sub", RoleClaimType = "role"
            };
        });
        builder.Services.AddSingleton<IPushSubscriptionRepository>(subscriptions);
        builder.Services.Configure<WebPushOptions>(options => { options.Enabled = true; options.PublicKey = publicKey; });
        // Keep the same messaging and HTTP registration combination as Program.
        builder.Services.AddMessaging(typeof(NotificationMessageHandler).Assembly);
        builder.Services.AddPushNotificationApi();
        app = builder.Build();
        app.Services.UseMessaging();
        app.UseMiddleware<ValidationExceptionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseFastEndpoints(configuration => configuration.Endpoints.RoutePrefix = "api");
        await app.StartAsync();
        client = app.GetTestClient();
    }

    [Theory]
    [InlineData("students", "GET")]
    [InlineData("students", "PUT")]
    [InlineData("students", "DELETE")]
    [InlineData("teachers", "GET")]
    [InlineData("teachers", "PUT")]
    [InlineData("teachers", "DELETE")]
    public async Task PushEndpointsRequireAuthentication(string rolePath, string method)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), Path(rolePath, method == "GET" ? "public-key" : "subscriptions"));
        if (method != "GET") request.Content = JsonContent.Create(Registration());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(subscriptions.Saved);
        Assert.Empty(subscriptions.Removed);
    }

    [Theory]
    [InlineData("students")]
    [InlineData("teachers")]
    public async Task RegistrationAndRemovalUseAuthenticatedIdentity(string rolePath)
    {
        var subject = Authenticate(rolePath);
        subscriptions.Saved.Add(PushSubscription.Create("other-user", Endpoint, publicKey, auth));

        using var registered = await client.PutAsJsonAsync(Path(rolePath, "subscriptions") + "?userId=other-user", Registration());

        Assert.Equal(HttpStatusCode.NoContent, registered.StatusCode);
        var saved = Assert.Single(subscriptions.Saved, subscription => subscription.UserId.Value == subject);
        Assert.Equal(Endpoint, saved.Endpoint);
        Assert.Equal(publicKey, saved.P256dh);
        Assert.Equal(auth, saved.Auth);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, Path(rolePath, "subscriptions"))
            {
                Content = JsonContent.Create(new { endpoint = Endpoint, userId = "other-user" })
            };
            using var removed = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }
        Assert.Equal("other-user", Assert.Single(subscriptions.Saved).UserId.Value);
        Assert.All(subscriptions.Removed, removed => Assert.Equal(subject, removed.UserId));
    }

    [Theory]
    [InlineData("Student", "Bifrost", "teacher")]
    [InlineData("Teacher", "Other", "teacher")]
    [InlineData("Teacher", "Bifrost", null)]
    public async Task TeacherRegistrationEnforcesRoleScopeAndSubject(string role, string scope, string? subject)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(role, scope, subject));
        using var response = await client.PutAsJsonAsync(Path("teachers", "subscriptions"), Registration());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(subscriptions.Saved);
    }

    [Fact]
    public async Task StudentCookieCannotRegisterTeacherSubscription()
    {
        Authenticate("students");
        using var response = await client.PutAsJsonAsync(Path("teachers", "subscriptions"), Registration());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(subscriptions.Saved);
    }

    [Theory]
    [InlineData("students")]
    [InlineData("teachers")]
    public async Task PublicKeyResponseContainsOnlyPublicKeyAndIsUnavailableWhenDisabled(string rolePath)
    {
        Authenticate(rolePath);
        using var enabled = await client.GetAsync(Path(rolePath, "public-key"));
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        using var payload = JsonDocument.Parse(await enabled.Content.ReadAsStringAsync());
        Assert.Equal(publicKey, payload.RootElement.GetProperty("publicKey").GetString());
        Assert.Single(payload.RootElement.EnumerateObject());

        app.Services.GetRequiredService<IOptions<WebPushOptions>>().Value.Enabled = false;
        using var disabled = await client.GetAsync(Path(rolePath, "public-key"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/device")]
    [InlineData("https://localhost/push")]
    [InlineData("https://127.0.0.1/push")]
    [InlineData("https://192.168.1.1/push")]
    [InlineData("https://example.com/push")]
    [InlineData("https://fcm.googleapis.com.evil.example/push")]
    [InlineData("https://user:password@fcm.googleapis.com/push")]
    [InlineData("https://fcm.googleapis.com:8443/push")]
    public async Task RegistrationRejectsUnsafeOrUnsupportedPushEndpoints(string endpoint)
    {
        Authenticate("students");
        using var response = await client.PutAsJsonAsync(Path("students", "subscriptions"),
            new { endpoint, keys = new { p256dh = publicKey, auth } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(subscriptions.Saved);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("public-key")]
    [InlineData("invalid-point")]
    [InlineData("auth")]
    public async Task RegistrationRejectsMissingOrInvalidCryptographicKeys(string invalid)
    {
        Authenticate("students");
        var invalidPoint = new byte[65];
        invalidPoint[0] = 4;
        var keys = invalid == "missing" ? null : new
        {
            p256dh = invalid == "public-key" ? "invalid" : invalid == "invalid-point" ? Base64Url(invalidPoint) : publicKey,
            auth = invalid == "auth" ? Base64Url(new byte[15]) : auth
        };
        using var response = await client.PutAsJsonAsync(Path("students", "subscriptions"), new { endpoint = Endpoint, keys });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(subscriptions.Saved);
    }

    [Fact]
    public async Task CommandsUseValidationMiddlewareAndForwardCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await new RegisterPushSubscriptionCommand(Student, Endpoint, publicKey, auth).ExecuteAsync(cancellation.Token);
        Assert.Equal(cancellation.Token, subscriptions.LastToken);
        await new UnregisterPushSubscriptionCommand(Student, Endpoint).ExecuteAsync(cancellation.Token);
        Assert.Equal(cancellation.Token, subscriptions.LastToken);
        Assert.Empty(subscriptions.Saved);

        await Assert.ThrowsAsync<ValidationException>(() =>
            new RegisterPushSubscriptionCommand("", Endpoint, publicKey, auth).ExecuteAsync(default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            new UnregisterPushSubscriptionCommand(Student, "https://localhost/push").ExecuteAsync(default));
        Assert.Single(subscriptions.Removed);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RegisterPushSubscriptionCommand(Student, Endpoint, publicKey, auth).ExecuteAsync(cancellation.Token));
        Assert.Empty(subscriptions.Saved);
    }

    private object Registration() => new { endpoint = Endpoint, keys = new { p256dh = publicKey, auth }, userId = "other-user" };
    private static string Path(string rolePath, string operation) => $"/api/{rolePath}/notifications/push/{operation}";

    private string Authenticate(string rolePath)
    {
        if (rolePath == "teachers")
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
            return "teacher";
        }
        client.DefaultRequestHeaders.Add("Cookie", StudentCookieHandler.CookieName + "=" +
            app.Services.GetRequiredService<StudentIdentityCookie>().Protect(Student));
        return Student;
    }

    private string Token(string role = "Teacher", string scope = "Bifrost", string? subject = "teacher")
    {
        List<Claim> claims = [new("role", role), new("scope", scope)];
        if (subject is not null) claims.Add(new("sub", subject));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("gna-push-tests", "Bifrost", claims,
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)));
    }

    private static string NewPublicKey()
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var point = key.ExportParameters(false).Q;
        return Base64Url([4, .. point.X!, .. point.Y!]);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
    }

    private sealed class SubscriptionStore : IPushSubscriptionRepository
    {
        public List<PushSubscription> Saved { get; } = [];
        public List<(string UserId, string Endpoint)> Removed { get; } = [];
        public CancellationToken LastToken { get; private set; }
        public Task<IReadOnlyList<PushSubscription>> GetForUserAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PushSubscription>>(Saved.Where(subscription => subscription.UserId.Value == userId).ToArray());
        public Task SaveAsync(PushSubscription subscription, CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            Saved.Add(subscription);
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string userId, string endpoint, CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            Removed.Add((userId, endpoint));
            Saved.RemoveAll(subscription => subscription.UserId.Value == userId && subscription.Endpoint == endpoint);
            return Task.CompletedTask;
        }
    }
}

[CollectionDefinition("FastEndpoints", DisableParallelization = true)]
public sealed class PushSubscriptionApiCollection;
