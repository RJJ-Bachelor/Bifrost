using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Features.Teacher.GetQueue;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.KnowledgeBase.Application.Features.Common;
using EirService.KnowledgeBase.Application.Features.Internal.FaqAnswered;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using StudentHelpRequestSummary = EirService.HelpRequests.Application.Features.Student.GetHelpRequests.HelpRequestResult;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task RejectedFaqIsExcludedFromConversationReadsWhileHistoryAndSharedFaqArePreserved()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var second = await OtherStudentAsync(session.Code);
        using var ordinary = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Digital);
        var original = (await student.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{firstId}", FaqJsonOptions))!;
        clock.Now = clock.Now.AddMinutes(1);
        var secondId = await SubmitAsync(second, session.Id, HelpType.Digital);
        clock.Now = clock.Now.AddMinutes(1);
        var ordinaryId = await SubmitAsync(ordinary, session.Id, HelpType.Digital);
        var faqId = Guid.NewGuid();
        await DeliverGeneralizationAsync(new GeneralizedCreatedMessage(faqId, session.Id, 1,
            "A shared question that does not help the first student.", [firstId, secondId]));
        await CompleteDigitalRequestThroughTeacherAsync(session.Id, faqId, "A shared answer.");
        await PersistFaqAnswerFromOutboxAsync();
        var suggested = (await student.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{firstId}", FaqJsonOptions))!;
        Assert.Equal(3, suggested.Messages.Count);
        clock.Now = clock.Now.AddMinutes(1);

        using var reject = await student.PatchAsJsonAsync($"/api/students/requests/{firstId}/faq", new { Accept = false });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        AssertOriginalQuestionOnly((await reject.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!);

        foreach (var (client, role) in new[] { (student, "students"), (teacher, "teachers") })
            AssertOriginalQuestionOnly((await client.GetFromJsonAsync<HelpRequestResult>(
                $"/api/{role}/requests/{firstId}", FaqJsonOptions))!);
        var studentRequests = (await student.GetFromJsonAsync<StudentHelpRequestSummary[]>(
            $"/api/students/requests?sessionId={session.Id}", FaqJsonOptions))!;
        var summary = Assert.Single(studentRequests, request => request.Id == firstId);
        Assert.Equal(firstId, summary.Id);
        Assert.Equal("Pending", summary.Status);
        Assert.Equal(Assert.Single(original.Messages).Text, summary.Message);
        var queue = (await teacher.GetFromJsonAsync<TeacherQueueResult>(
            $"/api/teachers/sessions/{session.Id}/queue", FaqJsonOptions))!;
        Assert.Equal(2, queue.WaitingCount);
        Assert.Equal(new[] { firstId, ordinaryId }, queue.Requests.Select(request => request.Id));
        AssertOriginalQuestionOnly(queue.Requests[0]);

        var otherSource = (await second.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{secondId}", FaqJsonOptions))!;
        Assert.Equal(HelpRequestStatus.AwaitingConfirmation, otherSource.Status);
        Assert.Equal(suggested.Messages.Select(message => message.MessageType),
            otherSource.Messages.Select(message => message.MessageType));
        var canonical = (await teacher.GetFromJsonAsync<HelpRequestResult>(
            $"/api/teachers/requests/{faqId}", FaqJsonOptions))!;
        Assert.Equal(new[] { HelpRequestMessageType.FaqQuestion, HelpRequestMessageType.FaqAnswer },
            canonical.Messages.Select(message => message.MessageType));
        var faq = Assert.Single((await student.GetFromJsonAsync<FaqResult[]>(
            $"/api/students/sessions/{session.Id}/faqs"))!);
        Assert.Equal(faqId, faq.Id);
        Assert.Equal("A shared answer.", faq.Answer);
        Assert.Equal(2, faq.SourceCount);

        await using var scope = app.Services.CreateAsyncScope();
        var history = await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Messages
            .Where(message => message.HelpRequestId == firstId).OrderBy(message => message.SequenceNumber).ToArrayAsync();
        Assert.Equal(suggested.Messages.Select(message => message.Id), history.Select(message => message.Id));
        Assert.False(history[0].IsRejected);
        Assert.All(history.Skip(1), message => Assert.True(message.IsRejected));

        void AssertOriginalQuestionOnly(HelpRequestResult current)
        {
            Assert.Equal(firstId, current.Id);
            Assert.Equal(HelpRequestStatus.Pending, current.Status);
            Assert.Null(current.FaqAnswerId);
            Assert.Null(current.AssignedTeacherId);
            Assert.Equal(original.QueuedAt, current.QueuedAt);
            Assert.Equal(original.ReturnedToQueueAt, current.ReturnedToQueueAt);
            Assert.Equal(Assert.Single(original.Messages), Assert.Single(current.Messages));
        }
    }

    [PostgresFact]
    public async Task DirectAnswersAndFollowUpsAfterFaqRejectionStayVisibleWithoutResendingGeneralizationInput()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var second = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Digital);
        var secondId = await SubmitAsync(second, session.Id, HelpType.Digital);
        var original = Assert.Single((await student.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{firstId}", FaqJsonOptions))!.Messages);
        var faqId = Guid.NewGuid();
        await DeliverGeneralizationAsync(new GeneralizedCreatedMessage(faqId, session.Id, 1,
            "An unsuitable shared question.", [firstId, secondId]));
        await CompleteDigitalRequestThroughTeacherAsync(session.Id, faqId, "An unsuitable shared answer.");
        using var reject = await student.PatchAsJsonAsync($"/api/students/requests/{firstId}/faq", new { Accept = false });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        await CompleteDigitalRequestThroughTeacherAsync(session.Id, firstId, "The individual answer.");
        using var followUp = await student.PostAsJsonAsync($"/api/students/requests/{firstId}/messages",
            new { Text = "My follow-up question." });
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
        var reopened = (await followUp.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!;
        Assert.Equal(new[] { HelpRequestMessageType.StudentQuestion, HelpRequestMessageType.TeacherAnswer,
            HelpRequestMessageType.FollowUpQuestion }, reopened.Messages.Select(message => message.MessageType));
        await CompleteDigitalRequestThroughTeacherAsync(session.Id, firstId, "The follow-up answer.");

        foreach (var (client, role) in new[] { (student, "students"), (teacher, "teachers") })
        {
            var current = (await client.GetFromJsonAsync<HelpRequestResult>(
                $"/api/{role}/requests/{firstId}", FaqJsonOptions))!;
            Assert.Equal(HelpRequestStatus.CompletedByDigital, current.Status);
            Assert.Equal(new[] { HelpRequestMessageType.StudentQuestion, HelpRequestMessageType.TeacherAnswer,
                HelpRequestMessageType.FollowUpQuestion, HelpRequestMessageType.TeacherAnswer },
                current.Messages.Select(message => message.MessageType));
            Assert.Equal(new[] { original.Text, "The individual answer.", "My follow-up question.", "The follow-up answer." },
                current.Messages.Select(message => message.Text));
            Assert.Equal(original, current.Messages[0]);
            Assert.Equal(new[] { 1, 4, 5, 6 }, current.Messages.Select(message => message.SequenceNumber));
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var created = await db.Outbox.Where(message => message.RoutingKey == MessageRoutes.HelpRequestCreated).ToArrayAsync();
        var inputs = created.Select(message => JsonSerializer.Deserialize<HelpRequestCreatedMessage>(message.Payload, FaqJsonOptions)!).ToArray();
        Assert.Equal(2, inputs.Length);
        Assert.Equal(original.Text, Assert.Single(inputs, message => message.HelpRequestId == firstId).Message);
        var answers = (await db.Outbox.Where(message => message.RoutingKey == MessageRoutes.HelpRequestAnswered).ToArrayAsync())
            .Select(message => JsonSerializer.Deserialize<HelpRequestAnsweredMessage>(message.Payload, FaqJsonOptions)!).ToArray();
        Assert.Equal(2, answers.Length);
        Assert.Contains(answers, answer => answer.Question == original.Text && answer.Answer == "The individual answer.");
        Assert.Contains(answers, answer => answer.Question == "My follow-up question." && answer.Answer == "The follow-up answer.");
    }

    [PostgresFact]
    public async Task RejectingOneFaqHidesOnlyThatAttemptAndAcceptedLaterFaqRemainsVisibleAfterFollowUp()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var second = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Digital);
        var secondId = await SubmitAsync(second, session.Id, HelpType.Digital);
        var firstFaqId = Guid.NewGuid();
        await DeliverGeneralizationAsync(new GeneralizedCreatedMessage(firstFaqId, session.Id, 1,
            "The first shared question.", [firstId, secondId]));
        await CompleteDigitalRequestThroughTeacherAsync(session.Id, firstFaqId, "The first shared answer.");
        await PersistSpecificFaqAnswerFromOutboxAsync(firstFaqId);
        using var reject = await student.PatchAsJsonAsync($"/api/students/requests/{firstId}/faq", new { Accept = false });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        var secondFaqId = Guid.NewGuid();

        await DeliverGeneralizationAsync(new GeneralizedCreatedMessage(secondFaqId, session.Id, 1,
            "The second shared question.", [firstId, secondId]));
        var linkedAgain = (await student.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{firstId}", FaqJsonOptions))!;
        Assert.Equal(HelpRequestStatus.AwaitingFaq, linkedAgain.Status);
        Assert.Equal(secondFaqId, linkedAgain.FaqAnswerId);
        Assert.Equal(new[] { HelpRequestMessageType.StudentQuestion, HelpRequestMessageType.FaqQuestion },
            linkedAgain.Messages.Select(message => message.MessageType));
        Assert.Equal("The second shared question.", linkedAgain.Messages[1].Text);
        await CompleteDigitalRequestThroughTeacherAsync(session.Id, secondFaqId, "The second shared answer.");
        await PersistSpecificFaqAnswerFromOutboxAsync(secondFaqId);
        using var accept = await student.PatchAsJsonAsync($"/api/students/requests/{firstId}/faq", new { Accept = true });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var accepted = (await accept.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!;
        Assert.Equal(HelpRequestStatus.CompletedByFaq, accepted.Status);
        Assert.Equal(new[] { "Question", "The second shared question.", "The second shared answer." },
            accepted.Messages.Select(message => message.Text));
        using var followUp = await student.PostAsJsonAsync($"/api/students/requests/{firstId}/messages",
            new { Text = "A follow-up after accepting the second answer." });
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);

        foreach (var (client, role) in new[] { (student, "students"), (teacher, "teachers") })
        {
            var current = (await client.GetFromJsonAsync<HelpRequestResult>(
                $"/api/{role}/requests/{firstId}", FaqJsonOptions))!;
            Assert.Equal(HelpRequestStatus.Pending, current.Status);
            Assert.Null(current.FaqAnswerId);
            Assert.Equal(accepted.Messages.ToArray(), current.Messages.Take(3).ToArray());
            Assert.Equal(HelpRequestMessageType.FollowUpQuestion, current.Messages.Last().MessageType);
            Assert.Equal(4, current.Messages.Count);
        }
        var queue = (await teacher.GetFromJsonAsync<TeacherQueueResult>(
            $"/api/teachers/sessions/{session.Id}/queue", FaqJsonOptions))!;
        Assert.Equal(4, Assert.Single(queue.Requests, request => request.Id == firstId).Messages.Count);
        var faqs = (await student.GetFromJsonAsync<FaqResult[]>($"/api/students/sessions/{session.Id}/faqs"))!;
        Assert.Equal(new[] { firstFaqId, secondFaqId }.Order(), faqs.Select(faq => faq.Id).Order());
        await using var scope = app.Services.CreateAsyncScope();
        var history = await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Messages
            .Where(message => message.HelpRequestId == firstId).OrderBy(message => message.SequenceNumber).ToArrayAsync();
        Assert.Equal(6, history.Length);
        Assert.Equal(new[] { 2, 3 }, history.Where(message => message.IsRejected).Select(message => message.SequenceNumber));
    }

    private async Task PersistSpecificFaqAnswerFromOutboxAsync(Guid faqId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var messages = await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox
            .Where(message => message.RoutingKey == MessageRoutes.FaqAnswered).ToArrayAsync();
        var payload = Assert.Single(messages.Select(message => JsonSerializer.Deserialize<FaqAnsweredMessage>(
            message.Payload, FaqJsonOptions)!), message => message.FaqId == faqId);
        await scope.ServiceProvider.GetRequiredService<FaqAnsweredMessageHandler>().HandleAsync(payload, CancellationToken.None);
    }
}
