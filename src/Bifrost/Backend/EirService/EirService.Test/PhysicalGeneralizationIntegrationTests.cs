using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task MixedGeneralizationLinksAndNotifiesBothHelpTypesAndPhysicalStudentCanAccept()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var firstDigitalStudent = await OtherStudentAsync(session.Code);
        using var secondDigitalStudent = await OtherStudentAsync(session.Code);
        var physicalId = await SubmitAsync(student, session.Id, HelpType.Physical);
        var firstDigitalId = await SubmitAsync(firstDigitalStudent, session.Id, HelpType.Digital);
        var secondDigitalId = await SubmitAsync(secondDigitalStudent, session.Id, HelpType.Digital);
        var physicalBefore = (await student.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{physicalId}", FaqJsonOptions))!;
        var payload = new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, 1,
            "How do I stop this loop?", [physicalId, firstDigitalId, secondDigitalId]);

        await DeliverGeneralizationAsync(payload);

        foreach (var (client, id) in new[]
                 { (student, physicalId), (firstDigitalStudent, firstDigitalId), (secondDigitalStudent, secondDigitalId) })
        {
            var source = (await client.GetFromJsonAsync<HelpRequestResult>(
                $"/api/students/requests/{id}", FaqJsonOptions))!;
            Assert.Equal(HelpRequestStatus.AwaitingFaq, source.Status);
            Assert.Equal(payload.GeneralizationId, source.FaqAnswerId);
            Assert.Equal(payload.Question,
                Assert.Single(source.Messages, message => message.MessageType == HelpRequestMessageType.FaqQuestion).Text);
        }
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var faq = Assert.Single(await scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Faqs.ToArrayAsync());
            Assert.Equal(payload.HelpRequestIds.Order(), faq.HelpRequestIds);
        }
        var created = Assert.Single(await ReadNotificationsAsync(),
            item => item.Message.EventType == NotificationEventType.FaqCreated).Message;
        Assert.Equal("provider|opaque-teacher", created.UserId);
        Assert.Equal(payload.GeneralizationId, created.HelpRequestId);
        Assert.DoesNotContain(await ReadNotificationsAsync(), item => item.Message.UserId == physicalBefore.StudentId);
        var queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
        Assert.Equal(new[] { payload.GeneralizationId },
            queue.GetProperty("requests").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(1, queue.GetProperty("waitingCount").GetInt32());

        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, take.StatusCode);
        Assert.Equal(payload.GeneralizationId,
            (await take.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Id);
        using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{payload.GeneralizationId}/messages",
            new { Text = "Update the loop variable." });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        var answerNotices = (await ReadNotificationsAsync())
            .Where(item => item.Message.EventType == NotificationEventType.FaqAnswered).ToArray();
        Assert.Equal(new[] { physicalId, firstDigitalId, secondDigitalId }.Order(),
            answerNotices.Select(item => item.Message.HelpRequestId!.Value).Order());
        Assert.Equal(3, answerNotices.Select(item => item.Id).Distinct().Count());
        foreach (var (client, id) in new[]
                 { (student, physicalId), (firstDigitalStudent, firstDigitalId), (secondDigitalStudent, secondDigitalId) })
        {
            var source = (await client.GetFromJsonAsync<HelpRequestResult>(
                $"/api/students/requests/{id}", FaqJsonOptions))!;
            Assert.Equal(HelpRequestStatus.AwaitingConfirmation, source.Status);
            Assert.Equal("Update the loop variable.",
                Assert.Single(source.Messages, message => message.MessageType == HelpRequestMessageType.FaqAnswer).Text);
            var notice = Assert.Single(answerNotices, item => item.Message.HelpRequestId == id).Message;
            Assert.Equal(source.StudentId, notice.UserId);
            Assert.Equal(session.Id, notice.SessionId);
            Assert.Equal(1, notice.Generation);
            Assert.Equal("AwaitingConfirmation", notice.Status);
        }
        queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
        Assert.Empty(queue.GetProperty("requests").EnumerateArray());
        Assert.Equal(0, queue.GetProperty("waitingCount").GetInt32());
        using var accept = await student.PatchAsJsonAsync($"/api/students/requests/{physicalId}/faq", new { Accept = true });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var completed = (await accept.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!;
        Assert.Equal(HelpRequestStatus.Completed, completed.Status);
        Assert.Equal(HelpType.Physical, completed.HelpType);
        Assert.Equal(physicalBefore.RequestNumber, completed.RequestNumber);
        Assert.Equal(physicalBefore.QueuedAt, completed.QueuedAt);
        Assert.Null(completed.AssignedTeacherId);
        Assert.Null(completed.StartedAt);
        Assert.NotNull(completed.ClosedAt);

        await DeliverGeneralizationAsync(payload);
        var replayed = (await student.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{physicalId}", FaqJsonOptions))!;
        Assert.Equal(completed.Status, replayed.Status);
        Assert.Equal(completed.ClosedAt, replayed.ClosedAt);
        Assert.Equal(completed.Messages.ToArray(), replayed.Messages.ToArray());
        Assert.Equal(3, (await ReadNotificationsAsync()).Count(item => item.Message.EventType == NotificationEventType.FaqAnswered));
    }

    [PostgresFact]
    public async Task PhysicalFaqRejectionsRestoreOriginalQueueOrderAndContinueAsPhysicalHelp()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var other = await OtherStudentAsync(session.Code);
        using var earlier = await OtherStudentAsync(session.Code);
        using var later = await OtherStudentAsync(session.Code);
        var earlierId = await SubmitAsync(earlier, session.Id, HelpType.Physical);
        clock.Now = clock.Now.AddMinutes(1);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Physical);
        clock.Now = clock.Now.AddMinutes(1);
        var secondId = await SubmitAsync(other, session.Id, HelpType.Physical);
        clock.Now = clock.Now.AddMinutes(1);
        var laterId = await SubmitAsync(later, session.Id, HelpType.Physical);
        var firstBefore = (await student.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{firstId}", FaqJsonOptions))!;
        var secondBefore = (await other.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{secondId}", FaqJsonOptions))!;
        var payload = new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, 1,
            "A shared question from physical help.", [firstId, secondId]);

        await DeliverGeneralizationAsync(payload);
        await DeliverGeneralizationAsync(payload);

        foreach (var (client, id) in new[] { (student, firstId), (other, secondId) })
        {
            var source = (await client.GetFromJsonAsync<HelpRequestResult>(
                $"/api/students/requests/{id}", FaqJsonOptions))!;
            Assert.Equal(HelpType.Physical, source.HelpType);
            Assert.Equal(HelpRequestStatus.AwaitingFaq, source.Status);
            Assert.Equal(payload.GeneralizationId, source.FaqAnswerId);
            Assert.Single(source.Messages, message => message.MessageType == HelpRequestMessageType.FaqQuestion);
        }
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var faq = Assert.Single(await scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Faqs.ToArrayAsync());
            Assert.Equal(payload.GeneralizationId, faq.Id);
            Assert.Equal(payload.HelpRequestIds.Order(), faq.HelpRequestIds);
            Assert.Equal(payload.Question, faq.Question);
        }
        var queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
        Assert.Equal(new[] { payload.GeneralizationId, earlierId, laterId },
            queue.GetProperty("requests").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(3, queue.GetProperty("waitingCount").GetInt32());
        var teacherNotice = Assert.Single(await ReadNotificationsAsync(),
            item => item.Message.EventType == NotificationEventType.FaqCreated).Message;
        Assert.Equal("provider|opaque-teacher", teacherNotice.UserId);

        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(payload.GeneralizationId,
            (await take.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Id);
        using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{payload.GeneralizationId}/messages",
            new { Text = "A reusable explanation." });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        var notices = await ReadNotificationsAsync();
        Assert.Equal(new[] { firstId, secondId }.Order(), notices
            .Where(item => item.Message.EventType == NotificationEventType.FaqAnswered)
            .Select(item => item.Message.HelpRequestId!.Value).Order());
        // Confirm in reverse order: returning to the queue must follow the original order.
        foreach (var (client, original) in new[] { (other, secondBefore), (student, firstBefore) })
        {
            clock.Now = clock.Now.AddMinutes(1);
            using var reject = await client.PatchAsJsonAsync($"/api/students/requests/{original.Id}/faq", new { Accept = false });
            Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
            await AssertPhysicalRequestReturnedAsync(client, original);
        }

        await DeliverGeneralizationAsync(payload);
        await AssertPhysicalRequestReturnedAsync(student, firstBefore);
        await AssertPhysicalRequestReturnedAsync(other, secondBefore);
        Assert.Equal(2, (await ReadNotificationsAsync()).Count(item => item.Message.EventType == NotificationEventType.FaqAnswered));
        queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
        Assert.Equal(new[] { earlierId, firstId, secondId, laterId },
            queue.GetProperty("requests").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(4, queue.GetProperty("waitingCount").GetInt32());
        using var takeEarlier = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(earlierId, (await takeEarlier.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Id);
        using var takePhysical = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        var reserved = (await takePhysical.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!;
        Assert.Equal(firstId, reserved.Id);
        Assert.Equal(HelpType.Physical, reserved.HelpType);
        using var invalidDigitalAnswer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{firstId}/messages",
            new { Text = "An individual digital answer is still not allowed." });
        Assert.Equal(HttpStatusCode.Conflict, invalidDigitalAnswer.StatusCode);
        using var start = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{firstId}/status", new { Action = "Start" });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal(HelpRequestStatus.Active, (await start.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Status);
        using var complete = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{firstId}/status", new { Action = "Complete" });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.Equal(HelpRequestStatus.Completed, (await complete.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Status);
    }

    [PostgresFact]
    public async Task GeneralizationDoesNotCaptureReservedActiveOrCompletedPhysicalHelp()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var activeStudent = await OtherStudentAsync(session.Code);
        using var reservedStudent = await OtherStudentAsync(session.Code);
        using var waitingStudent = await OtherStudentAsync(session.Code);
        var completedId = await SubmitAsync(student, session.Id, HelpType.Physical);
        await ReservePhysicalAsync(completedId);
        using var completedStart = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{completedId}/status", new { Action = "Start" });
        Assert.Equal(HttpStatusCode.OK, completedStart.StatusCode);
        using var complete = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{completedId}/status", new { Action = "Complete" });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var activeId = await SubmitAsync(activeStudent, session.Id, HelpType.Physical);
        await ReservePhysicalAsync(activeId);
        using var activeStart = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{activeId}/status", new { Action = "Start" });
        Assert.Equal(HttpStatusCode.OK, activeStart.StatusCode);
        var reservedId = await SubmitAsync(reservedStudent, session.Id, HelpType.Physical);
        await ReservePhysicalAsync(reservedId);
        var waitingId = await SubmitAsync(waitingStudent, session.Id, HelpType.Physical);
        var unchanged = new List<(HttpClient Client, HelpRequestResult Before)>();
        foreach (var (client, id) in new[] { (student, completedId), (activeStudent, activeId), (reservedStudent, reservedId) })
            unchanged.Add((client, (await client.GetFromJsonAsync<HelpRequestResult>(
                $"/api/students/requests/{id}", FaqJsonOptions))!));
        var payload = new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, 1,
            "The common question.", [completedId, activeId, reservedId, waitingId]);

        await DeliverGeneralizationAsync(payload);
        await DeliverGeneralizationAsync(payload);
        await CompleteDigitalRequestThroughTeacherAsync(session.Id, payload.GeneralizationId, "The common answer.");

        foreach (var (client, before) in unchanged)
        {
            var current = (await client.GetFromJsonAsync<HelpRequestResult>(
                $"/api/students/requests/{before.Id}", FaqJsonOptions))!;
            Assert.Equal(before.Status, current.Status);
            Assert.Equal(before.AssignedTeacherId, current.AssignedTeacherId);
            Assert.Equal(before.QueuedAt, current.QueuedAt);
            Assert.Equal(before.StartedAt, current.StartedAt);
            Assert.Equal(before.ClosedAt, current.ClosedAt);
            Assert.Equal(before.Messages.ToArray(), current.Messages.ToArray());
            Assert.Null(current.FaqAnswerId);
        }
        var waiting = (await waitingStudent.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{waitingId}", FaqJsonOptions))!;
        Assert.Equal(HelpType.Physical, waiting.HelpType);
        Assert.Equal(HelpRequestStatus.AwaitingConfirmation, waiting.Status);
        Assert.Equal(payload.GeneralizationId, waiting.FaqAnswerId);
        Assert.Equal(waitingId, Assert.Single(await ReadNotificationsAsync(),
            notice => notice.Message.EventType == NotificationEventType.FaqAnswered).Message.HelpRequestId);

        async Task ReservePhysicalAsync(Guid id)
        {
            using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
            Assert.Equal(HttpStatusCode.OK, take.StatusCode);
            Assert.Equal(id, (await take.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Id);
        }
    }

    [PostgresFact]
    public async Task PhysicalAndDigitalInitialQuestionsArePublishedOnceWhileAllFollowUpsStayOutOfGeneralization()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var other = await OtherStudentAsync(session.Code);
        var physicalId = await SubmitAsync(student, session.Id, HelpType.Physical);
        var digitalId = await SubmitAsync(other, session.Id, HelpType.Digital);

        foreach (var (client, id) in new[] { (student, physicalId), (other, digitalId) })
        {
            using var pendingFollowUp = await client.PostAsJsonAsync($"/api/students/requests/{id}/messages",
                new { Text = "Additional details while waiting." });
            Assert.Equal(HttpStatusCode.OK, pendingFollowUp.StatusCode);
        }
        var reservedIds = new List<Guid>();
        for (var index = 0; index < 2; index++)
        {
            using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
            Assert.Equal(HttpStatusCode.OK, take.StatusCode);
            reservedIds.Add((await take.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Id);
        }
        Assert.Equal(new[] { physicalId, digitalId }.Order(), reservedIds.Order());
        using var start = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{physicalId}/status",
            new { Action = "Start" });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        using var complete = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{physicalId}/status",
            new { Action = "Complete" });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{digitalId}/messages",
            new { Text = "The teacher's answer." });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        foreach (var (client, id) in new[] { (student, physicalId), (other, digitalId) })
        {
            using var reopen = await client.PostAsJsonAsync($"/api/students/requests/{id}/messages",
                new { Text = "A new follow-up after help finished." });
            Assert.Equal(HttpStatusCode.OK, reopen.StatusCode);
            using var moreDetails = await client.PostAsJsonAsync($"/api/students/requests/{id}/messages",
                new { Text = "Further details about the follow-up." });
            Assert.Equal(HttpStatusCode.OK, moreDetails.StatusCode);
            var source = (await client.GetFromJsonAsync<HelpRequestResult>(
                $"/api/students/requests/{id}", FaqJsonOptions))!;
            Assert.Equal(HelpRequestStatus.Pending, source.Status);
            Assert.Single(source.Messages, message => message.MessageType == HelpRequestMessageType.StudentQuestion);
            Assert.Equal(3, source.Messages.Count(message => message.MessageType == HelpRequestMessageType.FollowUpQuestion));
        }

        await using var scope = app.Services.CreateAsyncScope();
        var outgoing = await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.AsNoTracking()
            .Where(message => message.RoutingKey == MessageRoutes.HelpRequestCreated).ToArrayAsync();
        Assert.Equal(2, outgoing.Length);
        foreach (var id in new[] { physicalId, digitalId })
        {
            var message = Assert.Single(outgoing, item => item.MessageId == id);
            var payload = JsonSerializer.Deserialize<HelpRequestCreatedMessage>(message.Payload, FaqJsonOptions)!;
            Assert.Equal(id, payload.HelpRequestId);
            Assert.Equal(session.Id, payload.SessionId);
            Assert.Equal(1, payload.Generation);
            Assert.Equal("Question", payload.Message);
        }
    }

    private static async Task AssertPhysicalRequestReturnedAsync(HttpClient client, HelpRequestResult original)
    {
        var current = (await client.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{original.Id}", FaqJsonOptions))!;
        Assert.Equal(HelpType.Physical, current.HelpType);
        Assert.Equal(original.RequestNumber, current.RequestNumber);
        Assert.Equal(original.DisplayNumber, current.DisplayNumber);
        Assert.Equal(HelpRequestStatus.Pending, current.Status);
        Assert.Null(current.FaqAnswerId);
        Assert.Null(current.AssignedTeacherId);
        Assert.Equal(original.QueuedAt, current.QueuedAt);
        Assert.Equal(original.ReturnedToQueueAt, current.ReturnedToQueueAt);
        Assert.Equal(original.Messages.ToArray(), current.Messages.ToArray());
        Assert.DoesNotContain(current.Messages,
            message => message.MessageType is HelpRequestMessageType.FaqQuestion or HelpRequestMessageType.FaqAnswer);
    }
}
