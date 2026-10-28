using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.Api.Endpoints.Student.Participants.GetStudentIdentity;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Domain.Entities;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task PhysicalQueueChangesAndReservationsNotifyAllParticipants()
    {
        var session = await CreateAsync();
        using var colleague = await JoinTeacherAsync(session.TeacherCode);
        using var ownerJoin = await teacher.PostAsJsonAsync("/api/teachers/sessions/join", new { Code = session.TeacherCode });
        Assert.Equal(HttpStatusCode.OK, ownerJoin.StatusCode);
        await JoinAsync(session.Code);
        using var observer = await OtherStudentAsync(session.Code);
        var studentId = (await student.GetFromJsonAsync<StudentIdentityResponse>("/api/students/me"))!.UserId;
        var observerId = (await observer.GetFromJsonAsync<StudentIdentityResponse>("/api/students/me"))!.UserId;
        string[] recipients = ["provider|opaque-teacher", "colleague", studentId, observerId];
        var before = await NotificationCursorAsync();

        var id = await SubmitAsync(student, session.Id, HelpType.Physical);
        AssertPhysicalBroadcast(await NotificationsSinceAsync(before), session.Id, recipients,
            new Dictionary<string, (NotificationEventType, Guid)>
            {
                ["provider|opaque-teacher"] = (NotificationEventType.HelpRequestCreated, id),
                ["colleague"] = (NotificationEventType.HelpRequestCreated, id)
            });

        await TakeAsync();
        await StatusAsync("Start", new() { [studentId] = (NotificationEventType.HelpRequestStarted, id) });
        await StatusAsync("Cancel"); // A teacher returns the request to the queue.
        await TakeAsync();
        await StatusAsync("Start", new() { [studentId] = (NotificationEventType.HelpRequestStarted, id) });
        await StatusAsync("Complete");

        before = await NotificationCursorAsync();
        using var followUp = await student.PostAsJsonAsync($"/api/students/requests/{id}/messages", new { Text = "More help" });
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
        AssertPhysicalBroadcast(await NotificationsSinceAsync(before), session.Id, recipients,
            new Dictionary<string, (NotificationEventType, Guid)>
            {
                ["provider|opaque-teacher"] = (NotificationEventType.StudentMessageAdded, id),
                ["colleague"] = (NotificationEventType.StudentMessageAdded, id)
            });

        before = await NotificationCursorAsync();
        using var cancel = await student.PatchAsJsonAsync($"/api/students/requests/{id}/status", new { Action = "Cancel" });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        AssertPhysicalBroadcast(await NotificationsSinceAsync(before), session.Id, recipients);
        before = await NotificationCursorAsync();
        using var retry = await student.PatchAsJsonAsync($"/api/students/requests/{id}/status", new { Action = "Cancel" });
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Empty(await NotificationsSinceAsync(before));

        void AssertPhysicalBroadcast((Guid Id, NotificationCreatedMessage Message)[] notifications,
            Guid sessionId, string[] audience,
            IReadOnlyDictionary<string, (NotificationEventType EventType, Guid RequestId)>? personal = null,
            string[]? genericChanges = null) =>
            AssertSessionBroadcast(notifications, sessionId, audience, personal, genericChanges, studentId);

        async Task TakeAsync()
        {
            var cursor = await NotificationCursorAsync();
            using var response = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(id, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
            AssertPhysicalBroadcast(await NotificationsSinceAsync(cursor), session.Id, recipients, genericChanges: ["Reserved"]);
        }

        async Task StatusAsync(string action, Dictionary<string, (NotificationEventType, Guid)>? personal = null)
        {
            var cursor = await NotificationCursorAsync();
            using var response = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{id}/status", new { Action = action });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertPhysicalBroadcast(await NotificationsSinceAsync(cursor), session.Id, recipients, personal,
                genericChanges: [action switch { "Start" => "Started", "Cancel" => "ReturnedToQueue", _ => "Completed" }]);
        }
    }

    [PostgresFact]
    public async Task DigitalAnswerReplacesTheStudentsGenericUpdateAndDoesNotNotifyAnotherSessionsParticipants()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var observer = await OtherStudentAsync(session.Code);
        using var colleague = await JoinTeacherAsync(session.TeacherCode);
        using var foreignTeacher = TeacherClient("unrelated-teacher");
        using var created = await foreignTeacher.PostAsJsonAsync("/api/teachers/sessions", new { Title = "Unrelated session" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var foreignSession = await created.Content.ReadFromJsonAsync<JsonElement>();
        using var foreignStudent = await OtherStudentAsync(foreignSession.GetProperty("code").GetString()!);
        var id = await SubmitAsync(student, session.Id, HelpType.Digital);
        var source = (await student.GetFromJsonAsync<HelpRequestResult>($"/api/students/requests/{id}", FaqJsonOptions))!;
        var observerId = (await observer.GetFromJsonAsync<StudentIdentityResponse>("/api/students/me"))!.UserId;
        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, take.StatusCode);
        var before = await NotificationCursorAsync();

        using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{id}/messages", new { Text = "A private answer" });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        var notifications = await NotificationsSinceAsync(before);
        AssertSessionBroadcast(notifications, session.Id,
            ["provider|opaque-teacher", "colleague", source.StudentId!, observerId],
            new Dictionary<string, (NotificationEventType, Guid)>
            { [source.StudentId!] = (NotificationEventType.HelpRequestAnswered, id) });
        Assert.All(notifications, item => Assert.DoesNotContain("A private answer", item.Message.Messages));
        var personal = Assert.Single(notifications, item => item.Message.UserId == source.StudentId).Message;
        Assert.Equal("Answered", personal.Change);
        Assert.Equal("CompletedByDigital", personal.Status);
        before = await NotificationCursorAsync();
        using var retry = await teacher.PostAsJsonAsync($"/api/teachers/requests/{id}/messages", new { Text = "Duplicate answer" });
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Empty(await NotificationsSinceAsync(before));
    }

    [PostgresFact]
    public async Task GeneralizedAnswerWaitsForCanonicalFaqBeforeNotifyingEveryParticipantExactlyOnce()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var digital = await OtherStudentAsync(session.Code);
        using var observer = await OtherStudentAsync(session.Code);
        using var colleague = await JoinTeacherAsync(session.TeacherCode);
        var physicalId = await SubmitAsync(student, session.Id, HelpType.Physical);
        var digitalId = await SubmitAsync(digital, session.Id, HelpType.Digital);
        var physicalUser = (await student.GetFromJsonAsync<StudentIdentityResponse>("/api/students/me"))!.UserId;
        var digitalUser = (await digital.GetFromJsonAsync<StudentIdentityResponse>("/api/students/me"))!.UserId;
        var observerUser = (await observer.GetFromJsonAsync<StudentIdentityResponse>("/api/students/me"))!.UserId;
        string[] recipients = ["provider|opaque-teacher", "colleague", physicalUser, digitalUser, observerUser];
        var generalized = new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, 1,
            "A generalized question", [physicalId, digitalId]);
        var before = await NotificationCursorAsync();

        await DeliverGeneralizationAsync(generalized);

        AssertSessionBroadcast(await NotificationsSinceAsync(before), session.Id, recipients,
            new Dictionary<string, (NotificationEventType, Guid)>
            {
                ["provider|opaque-teacher"] = (NotificationEventType.FaqCreated, generalized.GeneralizationId),
                ["colleague"] = (NotificationEventType.FaqCreated, generalized.GeneralizationId),
                [physicalUser] = (NotificationEventType.FaqCreated, physicalId),
                [digitalUser] = (NotificationEventType.FaqCreated, digitalId)
            });
        before = await NotificationCursorAsync();
        await DeliverGeneralizationAsync(generalized);
        Assert.Empty(await NotificationsSinceAsync(before));

        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, take.StatusCode);
        Assert.Equal(generalized.GeneralizationId,
            (await take.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        before = await NotificationCursorAsync();
        using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{generalized.GeneralizationId}/messages",
            new { Text = "A shared answer" });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Empty(await NotificationsSinceAsync(before));
        var beforeProjection = await student.GetFromJsonAsync<JsonElement>($"/api/students/sessions/{session.Id}/faqs");
        Assert.Empty(beforeProjection.EnumerateArray());

        await PersistFaqAnswerFromOutboxAsync();

        var projected = await student.GetFromJsonAsync<JsonElement>($"/api/students/sessions/{session.Id}/faqs");
        Assert.Equal("A shared answer", Assert.Single(projected.EnumerateArray()).GetProperty("answer").GetString());
        AssertSessionBroadcast(await NotificationsSinceAsync(before), session.Id, recipients,
            new Dictionary<string, (NotificationEventType, Guid)>
            {
                [physicalUser] = (NotificationEventType.FaqAnswered, physicalId),
                [digitalUser] = (NotificationEventType.FaqAnswered, digitalId)
            });
        before = await NotificationCursorAsync();
        await PersistFaqAnswerFromOutboxAsync();
        Assert.Empty(await NotificationsSinceAsync(before));

        before = await NotificationCursorAsync();
        using var reject = await student.PatchAsJsonAsync($"/api/students/requests/{physicalId}/faq", new { Accept = false });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        AssertSessionBroadcast(await NotificationsSinceAsync(before), session.Id, recipients, genericChanges: ["FaqRejected"], requestOwner: physicalUser);
        before = await NotificationCursorAsync();
        using var accept = await digital.PatchAsJsonAsync($"/api/students/requests/{digitalId}/faq", new { Accept = true });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        AssertSessionBroadcast(await NotificationsSinceAsync(before), session.Id, ["provider|opaque-teacher", "colleague", digitalUser], genericChanges: ["FaqAccepted"], requestOwner: digitalUser);
    }

    private async Task<HashSet<Guid>> NotificationCursorAsync() =>
        (await ReadNotificationsAsync(null)).Select(item => item.Id).ToHashSet();

    private async Task<(Guid Id, NotificationCreatedMessage Message)[]> NotificationsSinceAsync(HashSet<Guid> cursor) =>
        (await ReadNotificationsAsync(null)).Where(item => !cursor.Contains(item.Id)).ToArray();

    private static void AssertSessionBroadcast(
        (Guid Id, NotificationCreatedMessage Message)[] notifications, Guid sessionId, string[] recipients,
        IReadOnlyDictionary<string, (NotificationEventType EventType, Guid RequestId)>? personal = null,
        string[]? genericChanges = null, string? requestOwner = null)
    {
        Assert.Equal(recipients.Order(), notifications.Select(item => item.Message.UserId).Order());
        Assert.Equal(recipients.Length, notifications.Select(item => item.Id).Distinct().Count());
        foreach (var (_, notification) in notifications)
        {
            Assert.Equal(sessionId, notification.SessionId);
            Assert.Equal(1, notification.Generation);
            if (personal is not null && personal.TryGetValue(notification.UserId, out var expected))
            {
                Assert.Equal(NotificationDeliveryType.RealtimeOrPush, notification.DeliveryType);
                Assert.Equal(expected.EventType, notification.EventType);
                Assert.Equal(expected.RequestId, notification.HelpRequestId);
                Assert.NotNull(notification.Status);
            }
            else
            {
                Assert.Equal(NotificationDeliveryType.RealtimeOnly, notification.DeliveryType);
                Assert.Equal(NotificationEventType.SessionChanged, notification.EventType);
                Assert.Contains(notification.Change, genericChanges ??
                    ["Created", "StudentMessageAdded", "Reserved", "Started", "Completed", "ReturnedToQueue",
                     "Cancelled", "Answered", "FaqLinked", "FaqCreated", "FaqAnswered", "FaqRejected", "FaqAccepted", "FaqCancelled"]);
                Assert.Equal("Sessionen er opdateret.", notification.Messages);
                if (notification.HelpRequestId is not null || notification.Status is not null)
                {
                    Assert.True(notification.UserId is "provider|opaque-teacher" or "colleague" || notification.UserId == requestOwner);
                    Assert.NotNull(notification.HelpRequestId);
                    Assert.NotNull(notification.Status);
                }
            }
        }
    }
}
