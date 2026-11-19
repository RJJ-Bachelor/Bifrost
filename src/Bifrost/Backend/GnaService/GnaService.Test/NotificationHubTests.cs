using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using GnaService.Api.Hubs;
using GnaService.Application.Notifications;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Shared.Api.Authentication;
using Shared.Application.Messaging.Contracts;
using Shared.Testing;
using GnaService.Test.Infrastructure;

namespace GnaService.Test;

public sealed partial class NotificationHubTests : IAsyncLifetime
{
    private readonly SymmetricSecurityKey key = new(RandomNumberGenerator.GetBytes(32));
    private WebApplication app = null!;
    private HttpClient client = null!;
    private const string Student = "11111111111111111111111111111111";
    private readonly ObservedConnectionRegistry connections = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddBifrostServiceAuthentication(builder.Configuration, builder.Environment);
        builder.Services.PostConfigure<StudentCookieOptions>(StudentCookieHandler.SchemeName,
            options => options.DataProtectionProvider = new EphemeralDataProtectionProvider());
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = null;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = "gna-tests", ValidateAudience = true, ValidAudience = "Bifrost",
                ValidateIssuerSigningKey = true, IssuerSigningKey = key, ValidateLifetime = true,
                NameClaimType = "sub", RoleClaimType = "role"
            };
        });
        builder.Services.AddSingleton<IConnectionRegistry>(connections);
        builder.Services.AddNotificationRealtime();
        app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapNotificationHubs();
        await app.StartAsync();
        client = app.GetTestClient();
    }

    [Fact]
    public async Task HubRejectsMissingOrInvalidStudentCookie()
    {
        using var missing = await client.PostAsync(NotificationHubAuthentication.StudentPath + "/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", StudentCookieHandler.CookieName + "=invalid");
        using var invalid = await client.PostAsync(NotificationHubAuthentication.StudentPath + "/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    [Fact]
    public async Task TeacherHubAcceptsOnlyValidatedJwtWithTeacherRoleAndScope()
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        using var valid = await client.PostAsync(NotificationHubAuthentication.TeacherPath + "/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(scope: "other"));
        using var wrongScope = await client.PostAsync(NotificationHubAuthentication.TeacherPath + "/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Forbidden, wrongScope.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(role: "Student"));
        using var wrongRole = await client.PostAsync(NotificationHubAuthentication.TeacherPath + "/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
        using var invalid = await client.PostAsync(NotificationHubAuthentication.TeacherPath + "/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    [Fact]
    public async Task WebSocketUsesValidatedSubjectAndRegistersAndRemovesConnection()
    {
        using var socket = await ConnectStudentAsync(Student, "?userId=someone-else");
        var registry = app.Services.GetRequiredService<IConnectionRegistry>();
        Assert.True(registry.IsConnected(Student));
        Assert.False(registry.IsConnected("someone-else"));
        using var scope = app.Services.CreateScope();
        // Exercise frames larger than the receive buffer as well as the notification metadata.
        var notification = new NotificationDeliveryRequestedMessage(Guid.NewGuid(), Student, new string('x', 20_000), DateTimeOffset.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), 2, "FaqSuggested", "AwaitingConfirmation",
            Shared.Domain.Notifications.NotificationEventType.FaqAnswered);
        Assert.True(await scope.ServiceProvider.GetRequiredService<IRealtimeNotificationSender>().TrySendAsync(notification, default));
        using var frame = JsonDocument.Parse((await ReadAsync(socket)).TrimEnd('\u001e'));
        Assert.Equal("NotificationReceived", frame.RootElement.GetProperty("target").GetString());
        var payload = frame.RootElement.GetProperty("arguments")[0];
        Assert.Equal("FaqSuggested", payload.GetProperty("change").GetString());
        Assert.Equal("AwaitingConfirmation", payload.GetProperty("status").GetString());
        Assert.Equal(2, payload.GetProperty("generation").GetInt32());
        Assert.Equal("FaqAnswered", payload.GetProperty("eventType").GetString());
        var received = payload.Deserialize<NotificationDeliveryRequestedMessage>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(notification, received);
        await DisconnectAsync(socket);
        Assert.False(registry.IsConnected(Student));
    }

    [Fact]
    public async Task NotificationsDoNotReachAnotherStudent()
    {
        using var recipient = await ConnectStudentAsync(Student);
        using var other = await ConnectStudentAsync("22222222222222222222222222222222");
        using var scope = app.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IRealtimeNotificationSender>();
        Assert.True(await sender.TrySendAsync(new(Guid.NewGuid(), Student, "Private", DateTimeOffset.UtcNow), default));
        Assert.Contains("Private", await ReadAsync(recipient));
        // A later message to the other student is a barrier: their first frame must be their own.
        Assert.True(await sender.TrySendAsync(new(Guid.NewGuid(), "22222222222222222222222222222222",
            "Other student", DateTimeOffset.UtcNow), default));
        Assert.Contains("Other student", await ReadAsync(other));
    }

    [Fact]
    public async Task TeacherWebSocketCanAuthenticateWithQueryToken()
    {
        using var socket = await ConnectTeacherAsync("teacher");
        Assert.True(app.Services.GetRequiredService<IConnectionRegistry>().IsConnected("teacher"));
    }

    private async Task<WebSocket> ConnectStudentAsync(string userId, string query = "")
    {
        var cookie = app.Services.GetRequiredService<StudentIdentityCookie>().Protect(userId);
        var socketClient = app.GetTestServer().CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Cookie = StudentCookieHandler.CookieName + "=" + cookie;
        return await ConnectAsync(socketClient, NotificationHubAuthentication.StudentPath + query, userId);
    }

    private Task<WebSocket> ConnectTeacherAsync(string userId) => ConnectAsync(
        app.GetTestServer().CreateWebSocketClient(), NotificationHubAuthentication.TeacherPath +
        "?access_token=" + Token(subject: userId), userId);

    private async Task<WebSocket> ConnectAsync(WebSocketClient client, string path, string userId)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var registered = connections.ExpectConnection(userId);
        var socket = await client.ConnectAsync(new Uri("ws://localhost" + path), deadline.Token);
        try
        {
            await SignalRTestClient.HandshakeAsync(socket, deadline.Token);
            await registered.WaitAsync(deadline.Token);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task DisconnectAsync(WebSocket socket)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var removed = connections.ExpectDisconnection();
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", deadline.Token);
        await removed.WaitAsync(deadline.Token);
    }

    private static Task<string> ReadAsync(WebSocket socket, CancellationToken cancellationToken = default) =>
        SignalRTestClient.ReadAsync(socket, cancellationToken);

    private async Task AssertNoPendingNotificationsAsync(string[] users, WebSocket[] sockets)
    {
        // All earlier sends have completed. A marker must be next on every tab, including outsiders.
        using var scope = app.Services.CreateScope();
        var marker = Guid.NewGuid();
        await scope.ServiceProvider.GetRequiredService<IRealtimeSessionChangeSender>().SendAsync(marker,
            new(users, Guid.NewGuid(), 1, "TestBarrier"), default);
        foreach (var socket in sockets)
            Assert.Equal(marker, (await ReadNotificationAsync(socket)).NotificationId);
    }

    private string Token(string role = "Teacher", string scope = "Bifrost", string subject = "teacher") => new JwtSecurityTokenHandler().WriteToken(
        new JwtSecurityToken("gna-tests", "Bifrost", [new("sub", subject), new("role", role), new("scope", scope)],
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));

    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (app is not null) await app.DisposeAsync();
    }
}
