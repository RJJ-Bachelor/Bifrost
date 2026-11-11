using System.Net.WebSockets;
using GnaService.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace GnaService.Test;

public sealed partial class NotificationHubTests
{
    [Fact]
    public async Task OneSessionRefreshReachesOnlyItsAudienceAcrossEveryTabWithoutPrivateFields()
    {
        const string otherStudentId = "22222222222222222222222222222222";
        const string outsiderId = "33333333333333333333333333333333";
        using var teacher = await ConnectTeacherAsync("session-teacher");
        using var student = await ConnectStudentAsync(Student);
        using var secondTab = await ConnectStudentAsync(Student);
        using var otherStudent = await ConnectStudentAsync(otherStudentId);
        using var outsider = await ConnectStudentAsync(outsiderId);
        foreach (var user in new[] { "session-teacher", Student, otherStudentId, outsiderId })
            await WaitForConnectionAsync(user);
        using var scope = app.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IRealtimeSessionChangeSender>();
        var id = Guid.NewGuid();
        var message = new SessionChangedMessage(
            ["session-teacher", Student, Student, otherStudentId, "offline-student"], Guid.NewGuid(), 4, "Answered");

        await sender.SendAsync(id, message, default);

        WebSocket[] recipients = [teacher, student, secondTab, otherStudent];
        foreach (var recipient in recipients)
        {
            var received = await ReadNotificationAsync(recipient);
            Assert.Equal(id, received.NotificationId);
            Assert.Equal("", received.UserId);
            Assert.Equal(message.SessionId, received.SessionId);
            Assert.Equal(4, received.Generation);
            Assert.Equal("Answered", received.Change);
            Assert.Equal(NotificationEventType.SessionChanged, received.EventType);
            Assert.Equal(NotificationDeliveryType.RealtimeOnly, received.DeliveryType);
            Assert.Null(received.HelpRequestId);
            Assert.Null(received.Status);
        }
        // Duplicate audience IDs do not duplicate a tab's refresh, and nonmembers receive nothing.
        foreach (var socket in recipients.Append(outsider))
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadAsync(socket, deadline.Token));
        }
    }

    [Fact]
    public async Task SessionRefreshStillReachesTheRemainingTabAfterAnotherTabDisconnects()
    {
        using var firstTab = await ConnectStudentAsync(Student);
        using var secondTab = await ConnectStudentAsync(Student);
        await WaitForConnectionAsync(Student);
        await firstTab.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing one tab", default);
        using var scope = app.Services.CreateScope();
        var id = Guid.NewGuid();

        await scope.ServiceProvider.GetRequiredService<IRealtimeSessionChangeSender>().SendAsync(id,
            new([Student], Guid.NewGuid(), 1, "Created"), default);

        Assert.Equal(id, (await ReadNotificationAsync(secondTab)).NotificationId);
    }
}
