using System.Net.WebSockets;
using System.Text.Json;
using GnaService.Application.Notifications;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Api.Authentication;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace GnaService.Test;

public sealed partial class NotificationHubTests
{
    [Fact]
    public async Task SessionChangesReachEveryConnectedTeacherStudentAndTabExactlyOnce()
    {
        const string secondStudent = "22222222222222222222222222222222";
        using var firstTeacher = await ConnectTeacherAsync("first-teacher");
        using var secondTeacher = await ConnectTeacherAsync("second-teacher");
        using var firstStudent = await ConnectStudentAsync(Student);
        using var otherStudent = await ConnectStudentAsync(secondStudent);
        using var secondTab = await ConnectStudentAsync(Student);
        string[] users = ["first-teacher", "second-teacher", Student, secondStudent];
        foreach (var user in users) await WaitForConnectionAsync(user);
        (string User, WebSocket Socket)[] recipients =
        [
            (users[0], firstTeacher), (users[1], secondTeacher),
            (Student, firstStudent), (secondStudent, otherStudent), (Student, secondTab)
        ];
        using var scope = app.Services.CreateScope();
        var push = new UnusedPushSender();
        var dispatcher = new NotificationDispatcher(scope.ServiceProvider.GetRequiredService<IRealtimeNotificationSender>(),
            push, NullLogger<NotificationDispatcher>.Instance);
        var sessionId = Guid.NewGuid();

        foreach (var change in new[] { "Created", "Answered", "ReturnedToQueue" })
        {
            var notifications = users.ToDictionary(user => user, user => new NotificationDeliveryRequestedMessage(
                Guid.NewGuid(), user, "Session updated", DateTimeOffset.UtcNow, sessionId, Guid.NewGuid(), 1,
                change, EventType: change == "Answered" && user == Student
                    ? NotificationEventType.HelpRequestAnswered : NotificationEventType.SessionChanged,
                DeliveryType: change == "Answered" && user == Student
                    ? NotificationDeliveryType.RealtimeOrPush : NotificationDeliveryType.RealtimeOnly));
            await Task.WhenAll(notifications.Values.Select(notification => dispatcher.DispatchAsync(notification, default)));

            foreach (var (user, socket) in recipients)
                Assert.Equal(notifications[user], await ReadNotificationAsync(socket));
        }

        foreach (var (_, socket) in recipients)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadAsync(socket, deadline.Token));
        }
        Assert.Equal(0, push.Calls);
    }

    [Fact]
    public async Task DisconnectingOneTabKeepsTheOtherTabReceivingSessionChanges()
    {
        using var firstTab = await ConnectStudentAsync(Student);
        using var secondTab = await ConnectStudentAsync(Student);
        await WaitForConnectionAsync(Student);
        await firstTab.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing one tab", default);
        using var scope = app.Services.CreateScope();
        var notification = new NotificationDeliveryRequestedMessage(Guid.NewGuid(), Student, "Session updated",
            DateTimeOffset.UtcNow, Guid.NewGuid(), EventType: NotificationEventType.SessionChanged,
            DeliveryType: NotificationDeliveryType.RealtimeOnly);

        var sent = await scope.ServiceProvider.GetRequiredService<IRealtimeNotificationSender>()
            .TrySendAsync(notification, default);

        Assert.True(sent);
        Assert.True(app.Services.GetRequiredService<IConnectionRegistry>().IsConnected(Student));
        Assert.Equal(notification, await ReadNotificationAsync(secondTab));
    }

    private async Task<WebSocket> ConnectTeacherAsync(string userId)
    {
        var socketClient = app.GetTestServer().CreateWebSocketClient();
        var socket = await socketClient.ConnectAsync(new Uri("ws://localhost" + NotificationHubAuthentication.TeacherPath +
            "?access_token=" + Token(subject: userId)), default);
        await HandshakeAsync(socket);
        return socket;
    }

    private static async Task<NotificationDeliveryRequestedMessage> ReadNotificationAsync(WebSocket socket)
    {
        using var frame = JsonDocument.Parse((await ReadAsync(socket)).TrimEnd('\u001e'));
        Assert.Equal("NotificationReceived", frame.RootElement.GetProperty("target").GetString());
        return frame.RootElement.GetProperty("arguments")[0].Deserialize<NotificationDeliveryRequestedMessage>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private sealed class UnusedPushSender : IPushNotificationSender
    {
        public int Calls { get; private set; }
        public Task<bool> TrySendAsync(NotificationDeliveryRequestedMessage notification, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(true);
        }
    }
}