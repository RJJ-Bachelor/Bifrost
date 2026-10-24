using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.KnowledgeBase.Domain.Entities;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using EirService.Sessions.Application.Features.Common;
using Microsoft.Extensions.DependencyInjection;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task ArchiveListCountsAreGroupedByGenerationAndRespectRoleAndSession()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var studentIdentity = await student.GetFromJsonAsync<JsonElement>("/api/students/me");
        var studentId = studentIdentity.GetProperty("userId").GetString()!;
        var anotherSession = await CreateAsync("Other session");
        await SeedAsync(session.Id, 1, studentId, answered: true);
        await SeedAsync(anotherSession.Id, 1, studentId, answered: true);
        using (var scope = app.Services.CreateScope())
        {
            var knowledge = scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>();
            knowledge.Faqs.Add(FaqAnswer.Create(Guid.NewGuid(), session.Id, 1, "Unanswered FAQ",
                [Guid.NewGuid(), Guid.NewGuid()], clock.Now));
            await knowledge.SaveChangesAsync();
        }
        using var firstArchive = await ArchiveAsync(session.Id, 1);
        Assert.Equal(HttpStatusCode.OK, firstArchive.StatusCode);
        await SeedAsync(session.Id, 2, "other-student", answered: false);
        using var secondArchive = await ArchiveAsync(session.Id, 2);
        Assert.Equal(HttpStatusCode.OK, secondArchive.StatusCode);
        // Current-generation content must not be counted in either archived generation.
        await SeedAsync(session.Id, 3, studentId, answered: true);

        var teacherList = (await teacher.GetFromJsonAsync<SessionArchiveSummary[]>(
            $"/api/teachers/sessions/{session.Id}/archives"))!;
        Assert.Equal(new[] { (2, 1, 1), (1, 1, 2) },
            teacherList.Select(archive => (archive.Generation, archive.PrivateConversationCount, archive.GeneralizedQuestionCount)));
        var studentList = (await student.GetFromJsonAsync<SessionArchiveSummary[]>(
            $"/api/students/sessions/{session.Id}/archives"))!;
        Assert.Equal(new[] { (2, 0, 0), (1, 1, 1) },
            studentList.Select(archive => (archive.Generation, archive.PrivateConversationCount, archive.GeneralizedQuestionCount)));
        foreach (var archive in teacherList)
        {
            var detail = (await teacher.GetFromJsonAsync<SessionArchiveResult>(
                $"/api/teachers/sessions/{session.Id}/archives/{archive.Id}"))!;
            Assert.Equal(detail.Conversations.Count, archive.PrivateConversationCount);
            Assert.Equal(detail.Faqs.Count, archive.GeneralizedQuestionCount);
        }

        async Task SeedAsync(Guid sessionId, int generation, string subjectId, bool answered)
        {
            using var scope = app.Services.CreateScope();
            var requests = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            var request = HelpRequest.Create(sessionId, generation, subjectId, HelpType.Digital,
                new MessageText("Private question"), clock.Now);
            request.Reserve(session.CreatedByTeacherId, clock.Now);
            request.Answer(session.CreatedByTeacherId, new MessageText("Private answer"), clock.Now);
            request.ClearDomainEvents();
            requests.Requests.Add(request);
            await requests.SaveChangesAsync();
            var knowledge = scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>();
            var faq = FaqAnswer.Create(Guid.NewGuid(), sessionId, generation, "Generalized question",
                [Guid.NewGuid(), Guid.NewGuid()], clock.Now);
            if (answered) faq.RecordAnswer("Generalized answer", session.CreatedByTeacherId, clock.Now);
            knowledge.Faqs.Add(faq);
            await knowledge.SaveChangesAsync();
        }
    }
}
