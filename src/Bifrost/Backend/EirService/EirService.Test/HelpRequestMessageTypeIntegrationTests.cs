using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.HelpRequests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task RejectedFaqAndDirectAnswerExposeDistinctTypesAndPublishTheStudentsQuestion()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var second = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Digital);
        var secondId = await SubmitAsync(second, session.Id, HelpType.Digital);
        var original = (await student.GetFromJsonAsync<HelpRequestResult>(
            $"/api/students/requests/{firstId}", FaqJsonOptions))!.Messages.Single().Text;
        var faqId = Guid.NewGuid();
        await DeliverGeneralizationAsync(new GeneralizedCreatedMessage(faqId, session.Id, 1, "Shared question", [firstId, secondId]));
        using var takeFaq = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(faqId, (await takeFaq.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Id);
        using var faqAnswer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{faqId}/messages", new { Text = "Shared answer" });
        Assert.Equal(HttpStatusCode.OK, faqAnswer.StatusCode);
        using var reject = await student.PatchAsJsonAsync($"/api/students/requests/{firstId}/faq", new { Accept = false });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(firstId, (await take.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Id);
        using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{firstId}/messages", new { Text = "Individual answer" });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        using var followUp = await student.PostAsJsonAsync($"/api/students/requests/{firstId}/messages", new { Text = "Follow-up question" });
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
        using var takeAgain = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, takeAgain.StatusCode);
        using var answerAgain = await teacher.PostAsJsonAsync($"/api/teachers/requests/{firstId}/messages", new { Text = "Follow-up answer" });
        Assert.Equal(HttpStatusCode.OK, answerAgain.StatusCode);

        foreach (var (client, route) in new[] { (student, "students"), (teacher, "teachers") })
        {
            var json = await client.GetFromJsonAsync<JsonElement>($"/api/{route}/requests/{firstId}");
            Assert.Equal(new[] { "StudentQuestion", "FaqQuestion", "FaqAnswer", "TeacherAnswer", "FollowUpQuestion", "TeacherAnswer" },
                json.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("messageType").GetString()));
        }
        await using var scope = app.Services.CreateAsyncScope();
        var outgoing = await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox
            .Where(message => message.RoutingKey == MessageRoutes.HelpRequestAnswered).ToArrayAsync();
        var events = outgoing.Select(message => JsonSerializer.Deserialize<HelpRequestAnsweredMessage>(message.Payload, FaqJsonOptions)!).ToArray();
        Assert.Contains(events, message => message.Question == original && message.Answer == "Individual answer");
        Assert.Contains(events, message => message.Question == "Follow-up question" && message.Answer == "Follow-up answer");
    }

    [PostgresFact]
    public async Task MessageTypeMigrationBackfillsFaqHistoryAfterRejectionAndFollowUp()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var sessionId = Guid.NewGuid();
        var faqId = Guid.NewGuid();
        var source = HelpRequest.Create(sessionId, 1, "student", HelpType.Digital, new MessageText("Original"), clock.Now);
        source.LinkToFaq(faqId, new MessageText("Shared question"), clock.Now);
        source.SuggestFaq(faqId, "teacher", new MessageText("Shared answer"), clock.Now);
        source.RejectFaq("student", clock.Now);
        source.Reserve("teacher", clock.Now);
        source.Answer("teacher", new MessageText("Individual answer"), clock.Now);
        source.AddStudentMessage("student", new MessageText("Follow-up"), clock.Now);
        source.Reserve("teacher", clock.Now);
        source.Answer("teacher", new MessageText("Follow-up answer"), clock.Now);
        var faq = HelpRequest.CreateFaq(faqId, sessionId, 1, new MessageText("Shared question"), clock.Now);
        faq.Reserve("teacher", clock.Now);
        faq.Answer("teacher", new MessageText("Shared answer"), clock.Now);
        var expected = source.Messages.Concat(faq.Messages).ToDictionary(message => message.Id, message => message.MessageType);
        db.Requests.AddRange(source, faq);
        await db.SaveChangesAsync();

        await db.GetService<IMigrator>().MigrateAsync("20261020120931_AddFaqQueue");
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var saved = await db.Messages.Where(message => message.HelpRequestId == source.Id || message.HelpRequestId == faq.Id).ToArrayAsync();
        Assert.Equal(expected.Count, saved.Length);
        Assert.All(saved, message => Assert.Equal(expected[message.Id], message.MessageType));
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
