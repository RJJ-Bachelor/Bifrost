using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.HelpRequests.Domain.Entities;
using EirService.Sessions.Application.Features.Common;
using EirService.Sessions.Application.Features.Teacher.ArchiveSession;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task LateFaqAnswerOnlyInvalidatesArchivesAndDoesNotPromptAnOldConfirmation()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var second = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Physical);
        var secondId = await SubmitAsync(second, session.Id, HelpType.Digital);
        var generalized = new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, 1,
            "Shared question", [firstId, secondId]);
        await DeliverGeneralizationAsync(generalized);
        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, take.StatusCode);
        using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{generalized.GeneralizationId}/messages",
            new { Text = "A delayed shared answer" });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        // The answer is still in the HelpRequests outbox when its generation is archived.
        using var archiveResponse = await ArchiveAsync(session.Id, 1);
        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
        var archive = (await archiveResponse.Content.ReadFromJsonAsync<ArchiveSessionResult>())!;
        await SubmitAsync(student, session.Id, HelpType.Digital);
        var queueUrl = $"/api/students/sessions/{session.Id}/queue";
        var queueBefore = await student.GetFromJsonAsync<JsonElement>(queueUrl);
        var cursor = await NotificationCursorAsync();

        await PersistFaqAnswerFromOutboxAsync();

        var notices = await NotificationsSinceAsync(cursor);
        Assert.Equal(3, notices.Length);
        Assert.Equal(3, notices.Select(notice => notice.Message.UserId).Distinct().Count());
        Assert.All(notices, notice =>
        {
            Assert.Equal(session.Id, notice.Message.SessionId);
            Assert.Equal(2, notice.Message.Generation);
            Assert.Equal("ArchivedFaqAnswered", notice.Message.Change);
            Assert.Equal(NotificationEventType.SessionChanged, notice.Message.EventType);
            Assert.Equal(NotificationDeliveryType.RealtimeOnly, notice.Message.DeliveryType);
            Assert.Null(notice.Message.HelpRequestId);
            Assert.Null(notice.Message.Status);
        });
        var queueAfter = await student.GetFromJsonAsync<JsonElement>(queueUrl);
        Assert.Equal(queueBefore.GetRawText(), queueAfter.GetRawText());
        var detail = (await student.GetFromJsonAsync<SessionArchiveResult>(
            $"/api/students/sessions/{session.Id}/archives/{archive.ArchiveId}"))!;
        Assert.Equal("A delayed shared answer", Assert.Single(detail.Faqs).Answer);
        var summaries = (await student.GetFromJsonAsync<SessionArchiveSummary[]>(
            $"/api/students/sessions/{session.Id}/archives"))!;
        Assert.Equal(1, Assert.Single(summaries).GeneralizedQuestionCount);
    }
}
