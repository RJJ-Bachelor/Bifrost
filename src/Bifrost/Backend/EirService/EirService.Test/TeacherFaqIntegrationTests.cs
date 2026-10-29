using System.Net;
using System.Net.Http.Json;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.KnowledgeBase.Application.Features.Common;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using EirService.Sessions.Application.Features.Common;
using EirService.Sessions.Application.Features.Teacher.ArchiveSession;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Domain.Notifications;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task TeacherFaqIsPublishedImmediatelyWithTrustedMetadataAndNoQueueItem()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var cursor = await NotificationCursorAsync();

        using var response = await teacher.PostAsJsonAsync($"/api/teachers/sessions/{session.Id}/faqs", new
        {
            Question = " Teacher question ", Answer = " Immediate answer ",
            Generation = 88, TeacherId = "spoofed", AnsweredAt = DateTimeOffset.MinValue
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var faq = (await response.Content.ReadFromJsonAsync<FaqResult>())!;
        Assert.Equal(session.Id, faq.SessionId);
        Assert.Equal(1, faq.Generation);
        Assert.Equal("Teacher question", faq.Question);
        Assert.Equal("Immediate answer", faq.Answer);
        Assert.Equal(0, faq.SourceCount);
        Assert.Equal(clock.Now, faq.CreatedAt);
        Assert.Equal(clock.Now, faq.AnsweredAt);
        Assert.Equal(faq, Assert.Single((await teacher.GetFromJsonAsync<FaqResult[]>($"/api/teachers/sessions/{session.Id}/faqs"))!));
        Assert.Equal(faq, Assert.Single((await student.GetFromJsonAsync<FaqResult[]>($"/api/students/sessions/{session.Id}/faqs"))!));

        await using var scope = app.Services.CreateAsyncScope();
        var stored = Assert.Single(await scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Faqs.ToArrayAsync());
        Assert.Equal("provider|opaque-teacher", stored.AnsweredByTeacherId);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Requests
            .Where(request => request.SessionId == session.Id).ToArrayAsync());
        var notices = await NotificationsSinceAsync(cursor);
        Assert.Equal(2, notices.Length);
        Assert.All(notices, notice =>
        {
            Assert.Equal("FaqAnswered", notice.Message.Change);
            Assert.Equal(1, notice.Message.Generation);
            Assert.Equal(NotificationDeliveryType.RealtimeOnly, notice.Message.DeliveryType);
            Assert.Null(notice.Message.HelpRequestId);
        });
    }

    [PostgresFact]
    public async Task TeacherFaqFollowsSessionArchivesAndCurrentGenerationAfterClearing()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var url = $"/api/teachers/sessions/{session.Id}/faqs";
        using var first = await teacher.PostAsJsonAsync(url, new { Question = "Archived question", Answer = "Archived answer" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var archived = await ArchiveAsync(session.Id, 1);
        var archive = (await archived.Content.ReadFromJsonAsync<ArchiveSessionResult>())!;
        Assert.Empty((await teacher.GetFromJsonAsync<FaqResult[]>(url))!);
        var detail = (await student.GetFromJsonAsync<SessionArchiveResult>(
            $"/api/students/sessions/{session.Id}/archives/{archive.ArchiveId}"))!;
        Assert.Equal("Archived question", Assert.Single(detail.Faqs).Question);

        using var clear = await ClearAsync(session.Id, 2);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        using var current = await teacher.PostAsJsonAsync(url, new { Question = "Current question", Answer = "Current answer" });
        Assert.Equal(HttpStatusCode.Created, current.StatusCode);
        Assert.Equal(3, (await current.Content.ReadFromJsonAsync<FaqResult>())!.Generation);
        Assert.Equal("Current question", Assert.Single((await student.GetFromJsonAsync<FaqResult[]>(
            $"/api/students/sessions/{session.Id}/faqs"))!).Question);
    }

    [PostgresFact]
    public async Task TeacherFaqRejectsForeignTeachersStudentsMissingSessionsAndInvalidText()
    {
        var session = await CreateAsync();
        var url = $"/api/teachers/sessions/{session.Id}/faqs";
        using var foreign = app.GetTestClient();
        foreign.DefaultRequestHeaders.Add("X-Test-Role", "Teacher");
        foreign.DefaultRequestHeaders.Add("X-Test-Scope", "Bifrost");
        foreign.DefaultRequestHeaders.Add("X-Test-Subject", "foreign-teacher");
        using var denied = await foreign.PostAsJsonAsync(url, new { Question = "Question", Answer = "Answer" });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var studentDenied = await student.PostAsJsonAsync(url, new { Question = "Question", Answer = "Answer" });
        Assert.Equal(HttpStatusCode.Unauthorized, studentDenied.StatusCode);
        using var missing = await teacher.PostAsJsonAsync($"/api/teachers/sessions/{Guid.NewGuid()}/faqs",
            new { Question = "Question", Answer = "Answer" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        foreach (var (question, answer) in new[] { (" ", "Answer"), ("Question", " "),
                     (new string('q', 4001), "Answer"), ("Question", new string('a', 4001)) })
        {
            using var invalid = await teacher.PostAsJsonAsync(url, new { Question = question, Answer = answer });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Faqs.ToArrayAsync());
    }
}
