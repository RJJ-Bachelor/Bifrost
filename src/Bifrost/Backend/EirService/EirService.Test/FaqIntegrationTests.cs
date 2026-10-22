using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.KnowledgeBase.Application.Features.Common;
using EirService.KnowledgeBase.Application.Features.Internal.FaqAnswered;
using EirService.KnowledgeBase.Application.Features.Internal.RegisterGeneralization;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    private static readonly JsonSerializerOptions FaqJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [PostgresFact]
    public async Task GeneralizedFaqIsPrioritizedDeduplicatedAnsweredAndConfirmedIndividually()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var second = await OtherStudentAsync(session.Code);
        using var third = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Digital);
        clock.Now = clock.Now.AddMinutes(1);
        var secondId = await SubmitAsync(second, session.Id, HelpType.Digital);
        clock.Now = clock.Now.AddMinutes(1);
        var ordinaryId = await SubmitAsync(third, session.Id, HelpType.Digital);
        using var colleague = app.GetTestClient();
        colleague.DefaultRequestHeaders.Add("X-Test-Role", "Teacher");
        colleague.DefaultRequestHeaders.Add("X-Test-Scope", "Bifrost");
        colleague.DefaultRequestHeaders.Add("X-Test-Subject", "colleague");
        using var join = await colleague.PostAsJsonAsync("/api/teachers/sessions/join", new { Code = session.TeacherCode });
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        var payload = new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, 1, "How do loops terminate?", [firstId, secondId]);
        await DeliverGeneralizationAsync(payload);
        await DeliverGeneralizationAsync(payload);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var faq = Assert.Single(await scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Faqs.ToArrayAsync());
            Assert.Equal(payload.Question, faq.Question);
            Assert.Equal(payload.HelpRequestIds.Order(), faq.HelpRequestIds);
        }
        var notices = (await ReadNotificationsAsync()).Where(item => item.Message.Change == "FaqCreated").ToArray();
        Assert.Equal(new[] { "colleague", "provider|opaque-teacher" }, notices.Select(item => item.Message.UserId).Order());
        Assert.Equal(2, notices.Select(item => item.Id).Distinct().Count());
        Assert.All(notices, item =>
        {
            Assert.Equal(NotificationEventType.FaqCreated, item.Message.EventType);
            Assert.Equal(session.Id, item.Message.SessionId);
            Assert.Equal(1, item.Message.Generation);
            Assert.Equal(payload.GeneralizationId, item.Message.HelpRequestId);
        });
        using var queue = await teacher.GetAsync($"/api/teachers/sessions/{session.Id}/queue");
        var queueJson = await queue.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(payload.GeneralizationId, queueJson.GetProperty("requests")[0].GetProperty("id").GetGuid());
        Assert.Equal(2, queueJson.GetProperty("waitingCount").GetInt32());
        var linked = (await student.GetFromJsonAsync<HelpRequestResult>($"/api/students/requests/{firstId}", FaqJsonOptions))!;
        Assert.Equal(HelpRequestStatus.AwaitingFaq, linked.Status);
        foreach (var (client, id) in new[] { (student, firstId), (second, secondId) })
        {
            var source = (await client.GetFromJsonAsync<HelpRequestResult>(
                $"/api/students/requests/{id}", FaqJsonOptions))!;
            Assert.Equal(payload.GeneralizationId, source.FaqAnswerId);
            var question = Assert.Single(source.Messages, message => message.AuthorRole == AuthorRole.System);
            Assert.Equal(payload.Question, question.Text);
            Assert.Equal("mimir", question.AuthorId);
            Assert.Equal(2, question.SequenceNumber);
        }
        var ordinary = (await third.GetFromJsonAsync<HelpRequestResult>($"/api/students/requests/{ordinaryId}", FaqJsonOptions))!;
        Assert.Null(ordinary.FaqAnswerId);
        Assert.Single(ordinary.Messages);
        using var duplicateOpen = await student.PostAsJsonAsync($"/api/students/sessions/{session.Id}/requests", new { Message = "Another", HelpType = "Digital" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateOpen.StatusCode);
        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        var reserved = (await take.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!;
        Assert.True(reserved.IsFaq);
        Assert.Equal(payload.GeneralizationId, reserved.Id);
        using var deniedAnswer = await colleague.PostAsJsonAsync($"/api/teachers/requests/{reserved.Id}/messages", new { Text = "Wrong teacher" });
        Assert.Equal(HttpStatusCode.Conflict, deniedAnswer.StatusCode);
        using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{reserved.Id}/messages", new { Text = "Check the loop condition." });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        var suggested = (await student.GetFromJsonAsync<HelpRequestResult>($"/api/students/requests/{firstId}", FaqJsonOptions))!;
        Assert.Equal(HelpRequestStatus.AwaitingConfirmation, suggested.Status);
        Assert.Equal("Check the loop condition.", suggested.Messages.Last().Text);
        var answerNotices = (await ReadNotificationsAsync()).Where(item => item.Message.EventType == NotificationEventType.FaqAnswered).ToArray();
        Assert.Equal(new[] { firstId, secondId }.Order(), answerNotices.Select(item => item.Message.HelpRequestId!.Value).Order());
        Assert.Equal(2, answerNotices.Select(item => item.Id).Distinct().Count());
        foreach (var (client, id) in new[] { (student, firstId), (second, secondId) })
        {
            var source = (await client.GetFromJsonAsync<HelpRequestResult>($"/api/students/requests/{id}", FaqJsonOptions))!;
            var notice = Assert.Single(answerNotices, item => item.Message.HelpRequestId == id).Message;
            Assert.Equal(source.StudentId, notice.UserId);
            Assert.Equal(session.Id, notice.SessionId);
            Assert.Equal(1, notice.Generation);
            Assert.Equal("AwaitingConfirmation", notice.Status);
        }
        await PersistFaqAnswerFromOutboxAsync();
        var faqs = (await student.GetFromJsonAsync<FaqResult[]>($"/api/students/sessions/{session.Id}/faqs"))!;
        Assert.Equal("Check the loop condition.", Assert.Single(faqs).Answer);
        using var accept = await student.PatchAsJsonAsync($"/api/students/requests/{firstId}/faq", new { Accept = true });
        Assert.Equal(HelpRequestStatus.Completed, (await accept.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Status);
        using var reject = await second.PatchAsJsonAsync($"/api/students/requests/{secondId}/faq", new { Accept = false });
        var returned = (await reject.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!;
        Assert.Equal(HelpRequestStatus.Pending, returned.Status);
        Assert.Null(returned.FaqAnswerId);
        using var foreign = await third.PatchAsJsonAsync($"/api/students/requests/{firstId}/faq", new { Accept = true });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var twice = await student.PatchAsJsonAsync($"/api/students/requests/{firstId}/faq", new { Accept = true });
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        using var next = await colleague.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(secondId, (await next.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Id);
        using var clear = await ClearAsync(session.Id, 1);
        Assert.Empty((await student.GetFromJsonAsync<FaqResult[]>($"/api/students/sessions/{session.Id}/faqs"))!);
        using var late = await second.PatchAsJsonAsync($"/api/students/requests/{secondId}/faq", new { Accept = true });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
    }

    [PostgresFact]
    public async Task ConcurrentTeachersTakeFaqOnlyOnceAndCancelledStudentsAreNotResurrected()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var second = await OtherStudentAsync(session.Code);
        using var third = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Digital);
        var secondId = await SubmitAsync(second, session.Id, HelpType.Digital);
        var ordinaryId = await SubmitAsync(third, session.Id, HelpType.Digital);
        var payload = new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, 1, "Shared", [firstId, secondId]);
        await DeliverGeneralizationAsync(payload);
        using var cancel = await student.PatchAsJsonAsync($"/api/students/requests/{firstId}/status", new { Action = "Cancel" });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        using var colleague = app.GetTestClient();
        colleague.DefaultRequestHeaders.Add("X-Test-Role", "Teacher");
        colleague.DefaultRequestHeaders.Add("X-Test-Scope", "Bifrost");
        colleague.DefaultRequestHeaders.Add("X-Test-Subject", "colleague");
        using var join = await colleague.PostAsJsonAsync("/api/teachers/sessions/join", new { Code = session.TeacherCode });
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        var calls = await Task.WhenAll(teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null),
            colleague.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null));
        var results = new List<HelpRequestResult>();
        foreach (var response in calls)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                results.Add((await response.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!);
            }
        }
        Assert.Equal(new[] { ordinaryId, payload.GeneralizationId }.Order(), results.Select(result => result.Id).Order());
        var owner = results.Single(result => result.IsFaq).AssignedTeacherId == "colleague" ? colleague : teacher;
        using var answer = await owner.PostAsJsonAsync($"/api/teachers/requests/{payload.GeneralizationId}/messages", new { Text = "Shared answer" });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(HelpRequestStatus.Cancelled, (await student.GetFromJsonAsync<HelpRequestResult>($"/api/students/requests/{firstId}", FaqJsonOptions))!.Status);
        Assert.Equal(HelpRequestStatus.AwaitingConfirmation, (await second.GetFromJsonAsync<HelpRequestResult>($"/api/students/requests/{secondId}", FaqJsonOptions))!.Status);
        var notice = Assert.Single(await ReadNotificationsAsync(), item => item.Message.EventType == NotificationEventType.FaqAnswered);
        Assert.Equal(secondId, notice.Message.HelpRequestId);
    }

    [PostgresFact]
    public async Task GeneralizationCannotCaptureReservedRequestsOrSourcesFromAnotherSession()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var second = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Digital);
        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, take.StatusCode);
        var secondId = await SubmitAsync(second, session.Id, HelpType.Digital);
        await DeliverGeneralizationAsync(new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, 1, "Shared", [firstId, secondId]));
        var reserved = (await student.GetFromJsonAsync<HelpRequestResult>($"/api/students/requests/{firstId}", FaqJsonOptions))!;
        Assert.Equal(HelpRequestStatus.Pending, reserved.Status);
        Assert.Null(reserved.FaqAnswerId);
        Assert.Single(reserved.Messages);
        Assert.Equal(HelpRequestStatus.AwaitingFaq, (await second.GetFromJsonAsync<HelpRequestResult>($"/api/students/requests/{secondId}", FaqJsonOptions))!.Status);
        var otherSession = await CreateAsync("Other");
        var invalid = new GeneralizedCreatedMessage(Guid.NewGuid(), otherSession.Id, 1, "Invalid", [firstId, secondId]);
        await DeliverGeneralizationAsync(invalid);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Faqs.FindAsync(invalid.GeneralizationId));
    }

    [PostgresFact]
    public async Task ReusingAGeneralizationIdCannotReplaceItsQuestionOrCaptureDifferentSources()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var second = await OtherStudentAsync(session.Code);
        using var third = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Digital);
        var secondId = await SubmitAsync(second, session.Id, HelpType.Digital);
        var thirdId = await SubmitAsync(third, session.Id, HelpType.Digital);
        var payload = new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, 1, "Shared question", [firstId, secondId]);
        await DeliverGeneralizationAsync(payload);
        int outboxCount;
        await using (var scope = app.Services.CreateAsyncScope())
            outboxCount = await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.CountAsync();

        await DeliverGeneralizationAsync(payload with { Question = "A conflicting question" });
        await DeliverGeneralizationAsync(payload with { HelpRequestIds = [firstId, thirdId] });

        await using var verification = app.Services.CreateAsyncScope();
        var faq = Assert.Single(await verification.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Faqs.ToArrayAsync());
        Assert.Equal(payload.Question, faq.Question);
        Assert.Equal(payload.HelpRequestIds.Order(), faq.HelpRequestIds);
        var requests = verification.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        Assert.Equal(outboxCount, await requests.Outbox.CountAsync());
        Assert.Single(await requests.Requests.Where(request => request.Id == payload.GeneralizationId).ToArrayAsync());
        foreach (var (client, id) in new[] { (student, firstId), (second, secondId) })
        {
            var source = (await client.GetFromJsonAsync<HelpRequestResult>($"/api/students/requests/{id}", FaqJsonOptions))!;
            Assert.Equal(payload.GeneralizationId, source.FaqAnswerId);
            Assert.Equal(payload.Question, Assert.Single(source.Messages, message => message.AuthorRole == AuthorRole.System).Text);
        }
        var untouched = (await third.GetFromJsonAsync<HelpRequestResult>($"/api/students/requests/{thirdId}", FaqJsonOptions))!;
        Assert.Equal(HelpRequestStatus.Pending, untouched.Status);
        Assert.Null(untouched.FaqAnswerId);
        Assert.Single(untouched.Messages);
    }

    private async Task DeliverGeneralizationAsync(GeneralizedCreatedMessage payload)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RegisterGeneralizationMessageHandler>()
            .HandleAsync(new MessageEnvelope<GeneralizedCreatedMessage>(Guid.NewGuid(), payload), CancellationToken.None);
    }

    private async Task PersistFaqAnswerFromOutboxAsync()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var outgoing = Assert.Single(await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox
            .Where(message => message.RoutingKey == MessageRoutes.FaqAnswered).ToArrayAsync());
        var payload = JsonSerializer.Deserialize<FaqAnsweredMessage>(outgoing.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var handler = scope.ServiceProvider.GetRequiredService<FaqAnsweredMessageHandler>();
        await handler.HandleAsync(payload, CancellationToken.None);
        await handler.HandleAsync(payload, CancellationToken.None);
    }
}
