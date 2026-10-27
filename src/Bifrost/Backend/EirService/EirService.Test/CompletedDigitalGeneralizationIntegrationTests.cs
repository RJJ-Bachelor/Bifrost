using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Domain.Entities;
using EirService.KnowledgeBase.Application.Features.Common;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task AnsweredDigitalSourceRemainsCompletedWhilePendingSourceReceivesTheSharedFaqAnswer()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var waitingStudent = await OtherStudentAsync(session.Code);
        var completedId = await SubmitAsync(student, session.Id, HelpType.Digital);
        await CompleteDigitalRequestThroughTeacherAsync(session.Id, completedId, "Your original answer.");
        var completedBefore = (await student.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{completedId}", FaqJsonOptions))!;
        var noticesBefore = (await ReadNotificationsAsync())
            .Where(item => item.Message.UserId == completedBefore.StudentId)
            .Select(item => item.Id).ToArray();
        Assert.Single(noticesBefore);
        var pendingId = await SubmitAsync(waitingStudent, session.Id, HelpType.Digital);
        var payload = new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, 1,
            "The shared version of both questions.", [completedId, pendingId]);

        await DeliverGeneralizationAsync(payload);
        await DeliverGeneralizationAsync(payload);

        await AssertCompletedDigitalRequestUnchangedAsync(student, completedBefore);
        var pending = (await waitingStudent.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{pendingId}", FaqJsonOptions))!;
        Assert.Equal(HelpRequestStatus.AwaitingFaq, pending.Status);
        Assert.Equal(payload.GeneralizationId, pending.FaqAnswerId);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var faq = Assert.Single(await scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>()
                .Faqs.ToArrayAsync());
            Assert.Equal(payload.HelpRequestIds.Order(), faq.HelpRequestIds);
        }

        await CompleteDigitalRequestThroughTeacherAsync(session.Id, payload.GeneralizationId, "A reusable shared answer.");
        await PersistFaqAnswerFromOutboxAsync();

        await AssertCompletedDigitalRequestUnchangedAsync(student, completedBefore);
        pending = (await waitingStudent.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{pendingId}", FaqJsonOptions))!;
        Assert.Equal(HelpRequestStatus.AwaitingConfirmation, pending.Status);
        Assert.Equal("A reusable shared answer.", Assert.Single(pending.Messages,
            message => message.MessageType == HelpRequestMessageType.FaqAnswer).Text);
        var notices = await ReadNotificationsAsync();
        Assert.Equal(noticesBefore,
            notices.Where(item => item.Message.UserId == completedBefore.StudentId).Select(item => item.Id));
        var faqNotice = Assert.Single(notices, item => item.Message.EventType == NotificationEventType.FaqAnswered).Message;
        Assert.Equal(pendingId, faqNotice.HelpRequestId);
        Assert.Equal(pending.StudentId, faqNotice.UserId);
        Assert.Equal("AwaitingConfirmation", faqNotice.Status);

        var visibleFaq = Assert.Single((await student.GetFromJsonAsync<FaqResult[]>(
            $"/api/students/sessions/{session.Id}/faqs"))!);
        Assert.Equal(payload.GeneralizationId, visibleFaq.Id);
        Assert.Equal(payload.Question, visibleFaq.Question);
        Assert.Equal("A reusable shared answer.", visibleFaq.Answer);
        Assert.Equal(2, visibleFaq.SourceCount);
        foreach (var accept in new[] { true, false })
        {
            using var response = await student.PatchAsJsonAsync($"/api/students/requests/{completedId}/faq",
                new { Accept = accept });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            await AssertCompletedDigitalRequestUnchangedAsync(student, completedBefore);
        }
    }

    [PostgresFact]
    public async Task GeneralizationOfOnlyAnsweredDigitalSourcesIsVisibleWithoutNotifyingOrReopeningThem()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var otherStudent = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Digital);
        await CompleteDigitalRequestThroughTeacherAsync(session.Id, firstId, "First original answer.");
        var secondId = await SubmitAsync(otherStudent, session.Id, HelpType.Digital);
        await CompleteDigitalRequestThroughTeacherAsync(session.Id, secondId, "Second original answer.");
        var firstBefore = (await student.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{firstId}", FaqJsonOptions))!;
        var secondBefore = (await otherStudent.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{secondId}", FaqJsonOptions))!;
        var studentNoticesBefore = (await ReadNotificationsAsync())
            .Where(item => item.Message.UserId == firstBefore.StudentId || item.Message.UserId == secondBefore.StudentId)
            .Select(item => item.Id).Order().ToArray();
        Assert.Equal(2, studentNoticesBefore.Length);
        var payload = new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, 1,
            "A generalization of already answered questions.", [firstId, secondId]);

        await DeliverGeneralizationAsync(payload);
        await CompleteDigitalRequestThroughTeacherAsync(session.Id, payload.GeneralizationId, "A shared reference answer.");
        await PersistFaqAnswerFromOutboxAsync();

        await AssertCompletedDigitalRequestUnchangedAsync(student, firstBefore);
        await AssertCompletedDigitalRequestUnchangedAsync(otherStudent, secondBefore);
        var notices = await ReadNotificationsAsync();
        Assert.DoesNotContain(notices, item => item.Message.EventType == NotificationEventType.FaqAnswered);
        Assert.Equal(studentNoticesBefore, notices
            .Where(item => item.Message.UserId == firstBefore.StudentId || item.Message.UserId == secondBefore.StudentId)
            .Select(item => item.Id).Order());
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var faq = Assert.Single(await scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>()
                .Faqs.ToArrayAsync());
            Assert.Equal(payload.HelpRequestIds.Order(), faq.HelpRequestIds);
        }
        foreach (var client in new[] { student, otherStudent })
        {
            var visibleFaq = Assert.Single((await client.GetFromJsonAsync<FaqResult[]>(
                $"/api/students/sessions/{session.Id}/faqs"))!);
            Assert.Equal(payload.GeneralizationId, visibleFaq.Id);
            Assert.Equal("A shared reference answer.", visibleFaq.Answer);
            Assert.Equal(2, visibleFaq.SourceCount);
        }
    }

    [PostgresFact]
    public async Task DelayedGeneralizationOfAnsweredOriginalDoesNotCaptureItsReopenedFollowUp()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var waitingStudent = await OtherStudentAsync(session.Code);
        var reopenedId = await SubmitAsync(student, session.Id, HelpType.Digital);
        await CompleteDigitalRequestThroughTeacherAsync(session.Id, reopenedId, "The original question is answered.");
        clock.Now = clock.Now.AddMinutes(1);
        using var followUp = await student.PostAsJsonAsync($"/api/students/requests/{reopenedId}/messages",
            new { Text = "A follow-up that still needs its own answer." });
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
        var reopenedBefore = (await followUp.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!;
        Assert.Equal(HelpRequestStatus.Pending, reopenedBefore.Status);
        Assert.Equal(HelpRequestMessageType.FollowUpQuestion, reopenedBefore.Messages.Last().MessageType);
        var studentNoticesBefore = (await ReadNotificationsAsync())
            .Where(item => item.Message.UserId == reopenedBefore.StudentId).Select(item => item.Id).ToArray();
        Assert.Single(studentNoticesBefore);
        clock.Now = clock.Now.AddMinutes(1);
        var pendingId = await SubmitAsync(waitingStudent, session.Id, HelpType.Digital);
        var payload = new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, 1,
            "A delayed generalization of the original questions.", [reopenedId, pendingId]);

        await DeliverGeneralizationAsync(payload);

        await AssertReopenedFollowUpUnchangedAsync();
        var pending = (await waitingStudent.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{pendingId}", FaqJsonOptions))!;
        Assert.Equal(HelpRequestStatus.AwaitingFaq, pending.Status);
        Assert.Equal(payload.GeneralizationId, pending.FaqAnswerId);
        await CompleteDigitalRequestThroughTeacherAsync(session.Id, payload.GeneralizationId, "The common original answer.");
        await PersistFaqAnswerFromOutboxAsync();

        await AssertReopenedFollowUpUnchangedAsync();
        var queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
        Assert.Equal(reopenedId,
            Assert.Single(queue.GetProperty("requests").EnumerateArray()).GetProperty("id").GetGuid());
        var notices = await ReadNotificationsAsync();
        Assert.Equal(studentNoticesBefore,
            notices.Where(item => item.Message.UserId == reopenedBefore.StudentId).Select(item => item.Id));
        Assert.Equal(pendingId, Assert.Single(notices,
            item => item.Message.EventType == NotificationEventType.FaqAnswered).Message.HelpRequestId);
        var visibleFaq = Assert.Single((await student.GetFromJsonAsync<FaqResult[]>(
            $"/api/students/sessions/{session.Id}/faqs"))!);
        Assert.Equal(payload.GeneralizationId, visibleFaq.Id);
        Assert.Equal("The common original answer.", visibleFaq.Answer);
        Assert.Equal(2, visibleFaq.SourceCount);

        async Task AssertReopenedFollowUpUnchangedAsync()
        {
            var current = (await student.GetFromJsonAsync<HelpRequestResult>(
                $"/api/students/requests/{reopenedId}", FaqJsonOptions))!;
            Assert.Equal(HelpRequestStatus.Pending, current.Status);
            Assert.Null(current.FaqAnswerId);
            Assert.Equal(reopenedBefore.ClosedAt, current.ClosedAt);
            Assert.Equal(reopenedBefore.AssignedTeacherId, current.AssignedTeacherId);
            Assert.Equal(reopenedBefore.QueuedAt, current.QueuedAt);
            Assert.Equal(reopenedBefore.ReturnedToQueueAt, current.ReturnedToQueueAt);
            Assert.Equal(reopenedBefore.Messages.ToArray(), current.Messages.ToArray());
            Assert.DoesNotContain(current.Messages,
                message => message.MessageType is HelpRequestMessageType.FaqQuestion or HelpRequestMessageType.FaqAnswer);
        }
    }

    private async Task CompleteDigitalRequestThroughTeacherAsync(Guid sessionId, Guid requestId, string answerText)
    {
        using var take = await teacher.PostAsync($"/api/teachers/sessions/{sessionId}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, take.StatusCode);
        Assert.Equal(requestId, (await take.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Id);
        using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{requestId}/messages",
            new { Text = answerText });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(HelpRequestStatus.CompletedByDigital,
            (await answer.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Status);
    }

    private static async Task AssertCompletedDigitalRequestUnchangedAsync(HttpClient client, HelpRequestResult original)
    {
        var current = (await client.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{original.Id}", FaqJsonOptions))!;
        Assert.Equal(HelpType.Digital, current.HelpType);
        Assert.Equal(HelpRequestStatus.CompletedByDigital, current.Status);
        Assert.NotNull(original.ClosedAt);
        Assert.Equal(original.ClosedAt, current.ClosedAt);
        Assert.Equal(original.AssignedTeacherId, current.AssignedTeacherId);
        Assert.Equal(original.QueuedAt, current.QueuedAt);
        Assert.Equal(original.ReturnedToQueueAt, current.ReturnedToQueueAt);
        Assert.Null(current.FaqAnswerId);
        Assert.Equal(original.Messages.ToArray(), current.Messages.ToArray());
        Assert.Equal(HelpRequestMessageType.TeacherAnswer, current.Messages.Last().MessageType);
        Assert.DoesNotContain(current.Messages,
            message => message.MessageType is HelpRequestMessageType.FaqQuestion or HelpRequestMessageType.FaqAnswer);
    }
}
