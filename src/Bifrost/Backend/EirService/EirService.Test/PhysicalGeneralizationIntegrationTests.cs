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
    public async Task MixedGeneralizationOnlyLinksDigitalSourcesAndNotifiesThoseStudentsWhenAnswered()
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

        await AssertPhysicalRequestUnchangedAsync(student, physicalBefore);
        foreach (var (client, id) in new[]
                 { (firstDigitalStudent, firstDigitalId), (secondDigitalStudent, secondDigitalId) })
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
        Assert.Equal(new[] { payload.GeneralizationId, physicalId },
            queue.GetProperty("requests").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(2, queue.GetProperty("waitingCount").GetInt32());

        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, take.StatusCode);
        Assert.Equal(payload.GeneralizationId,
            (await take.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Id);
        using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{payload.GeneralizationId}/messages",
            new { Text = "Update the loop variable." });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        await AssertPhysicalRequestUnchangedAsync(student, physicalBefore);
        var answerNotices = (await ReadNotificationsAsync())
            .Where(item => item.Message.EventType == NotificationEventType.FaqAnswered).ToArray();
        Assert.Equal(new[] { firstDigitalId, secondDigitalId }.Order(),
            answerNotices.Select(item => item.Message.HelpRequestId!.Value).Order());
        Assert.Equal(2, answerNotices.Select(item => item.Id).Distinct().Count());
        foreach (var (client, id) in new[]
                 { (firstDigitalStudent, firstDigitalId), (secondDigitalStudent, secondDigitalId) })
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
        Assert.DoesNotContain(await ReadNotificationsAsync(), item => item.Message.UserId == physicalBefore.StudentId);
        queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
        Assert.Equal(physicalId,
            Assert.Single(queue.GetProperty("requests").EnumerateArray()).GetProperty("id").GetGuid());
        using var physicalTake = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(physicalId,
            (await physicalTake.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Id);
    }

    [PostgresFact]
    public async Task GeneralizationOfOnlyPhysicalSourcesCreatesTeacherFaqWithoutChangingPhysicalRequests()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var other = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Physical);
        clock.Now = clock.Now.AddMinutes(1);
        var secondId = await SubmitAsync(other, session.Id, HelpType.Physical);
        var firstBefore = (await student.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{firstId}", FaqJsonOptions))!;
        var secondBefore = (await other.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{secondId}", FaqJsonOptions))!;
        var payload = new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, 1,
            "A shared question from physical help.", [firstId, secondId]);

        await DeliverGeneralizationAsync(payload);
        await DeliverGeneralizationAsync(payload);

        await AssertPhysicalRequestUnchangedAsync(student, firstBefore);
        await AssertPhysicalRequestUnchangedAsync(other, secondBefore);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var faq = Assert.Single(await scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Faqs.ToArrayAsync());
            Assert.Equal(payload.GeneralizationId, faq.Id);
            Assert.Equal(payload.HelpRequestIds.Order(), faq.HelpRequestIds);
            Assert.Equal(payload.Question, faq.Question);
        }
        var queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
        Assert.Equal(new[] { payload.GeneralizationId, firstId, secondId },
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

        await AssertPhysicalRequestUnchangedAsync(student, firstBefore);
        await AssertPhysicalRequestUnchangedAsync(other, secondBefore);
        var notices = await ReadNotificationsAsync();
        Assert.DoesNotContain(notices, item => item.Message.EventType == NotificationEventType.FaqAnswered);
        Assert.DoesNotContain(notices, item => item.Message.UserId == firstBefore.StudentId ||
            item.Message.UserId == secondBefore.StudentId);
        queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
        Assert.Equal(new[] { firstId, secondId },
            queue.GetProperty("requests").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(2, queue.GetProperty("waitingCount").GetInt32());
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

    private static async Task AssertPhysicalRequestUnchangedAsync(HttpClient client, HelpRequestResult original)
    {
        var current = (await client.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{original.Id}", FaqJsonOptions))!;
        Assert.Equal(HelpType.Physical, current.HelpType);
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
