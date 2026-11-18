using System.IdentityModel.Tokens.Jwt;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using GnaService.Api.Hubs;
using GnaService.Application.Notifications;
using GnaService.Infrastructure.Notifications;
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

namespace GnaService.Test;

public sealed partial class NotificationHubTests : IAsyncLifetime
{
    private readonly SymmetricSecurityKey key = new(RandomNumberGenerator.GetBytes(32));
    private WebApplication app = null!;
    private HttpClient client = null!;
    private const string Student = "11111111111111111111111111111111";
    private static readonly ConditionalWeakTable<WebSocket, BufferedFrames> ReceivedFrames = new();

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
        builder.Services.AddSingleton<IConnectionRegistry, ConnectionRegistry>();
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
        await WaitForConnectionAsync(Student);
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
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", shutdown.Token);
        await WaitUntilAsync(() => !registry.IsConnected(Student), "The student connection was not removed.");
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
        var socketClient = app.GetTestServer().CreateWebSocketClient();
        using var socket = await socketClient.ConnectAsync(new Uri("ws://localhost" + NotificationHubAuthentication.TeacherPath + "?access_token=" + Token()), default);
        await HandshakeAsync(socket);
        await WaitForConnectionAsync("teacher");
        Assert.True(app.Services.GetRequiredService<IConnectionRegistry>().IsConnected("teacher"));
    }

    private async Task<WebSocket> ConnectStudentAsync(string userId, string query = "")
    {
        var cookie = app.Services.GetRequiredService<StudentIdentityCookie>().Protect(userId);
        var socketClient = app.GetTestServer().CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Cookie = StudentCookieHandler.CookieName + "=" + cookie;
        var socket = await socketClient.ConnectAsync(new Uri("ws://localhost" + NotificationHubAuthentication.StudentPath + query), default);
        try
        {
            await HandshakeAsync(socket);
            await WaitForConnectionAsync(userId);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task HandshakeAsync(WebSocket socket)
    {
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, default);
        Assert.Equal("{}\u001e", await ReadAsync(socket));
    }

    private async Task WaitForConnectionAsync(string userId)
    {
        var registry = app.Services.GetRequiredService<IConnectionRegistry>();
        await WaitUntilAsync(() => registry.IsConnected(userId), $"User '{userId}' was not registered as connected.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string failureMessage)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), failureMessage);
            await Task.Delay(10);
        }
    }

    private static async Task<string> ReadAsync(WebSocket socket, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var frames = ReceivedFrames.GetValue(socket, _ => new BufferedFrames());
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            var separator = frames.Pending.IndexOf(0x1e);
            if (separator >= 0)
            {
                var frame = frames.Pending.GetRange(0, separator + 1).ToArray();
                frames.Pending.RemoveRange(0, separator + 1);
                return Encoding.UTF8.GetString(frame);
            }
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(frames.ReadBuffer), timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("The SignalR connection closed before a complete frame was received.");
            frames.Pending.AddRange(frames.ReadBuffer.AsSpan(0, result.Count).ToArray());
        }
    }

    private sealed class BufferedFrames
    {
        public byte[] ReadBuffer { get; } = new byte[8192];
        public List<byte> Pending { get; } = [];
    }

    private string Token(string role = "Teacher", string scope = "Bifrost", string subject = "teacher") => new JwtSecurityTokenHandler().WriteToken(
        new JwtSecurityToken("gna-tests", "Bifrost", [new("sub", subject), new("role", role), new("scope", scope)],
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
    }
}
