using System.Text.Json;
using EirService.Contracts.HelpRequests.GetArchivedConversationCounts;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.HelpRequests.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task ArchivedConversationCountsMatchVisibleConversationsBeforeAndAfterQueueCleanup()
    {
        var session = await CreateAsync();
        var now = clock.Now;
        HelpRequest Personal(string studentId, int generation = 1, Guid? sessionId = null) =>
            HelpRequest.Create(sessionId ?? session.Id, generation, studentId, HelpType.Digital, new MessageText("Question"), now);
        HelpRequest Answered(string studentId, int generation = 1, Guid? sessionId = null)
        {
            var request = Personal(studentId, generation, sessionId);
            request.Reserve("teacher", now);
            request.Answer("teacher", new MessageText("Private answer"), now);
            return request;
        }
        var completed = Answered("completed");
        var reopened = Answered("reopened");
        reopened.AddStudentMessage("reopened", new MessageText("Follow-up"), now.AddMinutes(1));
        var cancelled = Personal("cancelled");
        cancelled.Cancel("cancelled", AuthorRole.Student, now);
        var faq = HelpRequest.CreateFaq(Guid.NewGuid(), session.Id, 1, new MessageText("Shared question"), now);
        faq.Reserve("teacher", now);
        faq.Answer("teacher", new MessageText("Shared answer"), now);
        var awaitingConfirmation = Personal("faq-student");
        awaitingConfirmation.LinkToFaq(faq.Id, new MessageText("Shared question"), now);
        awaitingConfirmation.SuggestFaq(faq.Id, "teacher", new MessageText("Shared answer"), now);
        var rejected = Personal("rejected");
        rejected.LinkToFaq(faq.Id, new MessageText("Shared question"), now);
        rejected.SuggestFaq(faq.Id, "teacher", new MessageText("Rejected answer"), now);
        rejected.RejectFaq("rejected", now);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            db.Requests.AddRange(completed, reopened, cancelled, faq, awaitingConfirmation, rejected,
                Personal("waiting"), Answered("completed", 2), Answered("other-session", sessionId: Guid.NewGuid()));
            await db.SaveChangesAsync();
            var archives = scope.ServiceProvider.GetRequiredService<IArchivedConversationRepository>();
            var original = await archives.GetForGenerationAsync(session.Id, 1, null, CancellationToken.None);
            Assert.Equal(4, original.Count);
            var duplicate = original.Single(conversation => conversation.Id == completed.Id);
            var snapshotOnly = duplicate with { Id = Guid.NewGuid(), StudentId = "snapshot-only" };
            foreach (var conversation in new[] { duplicate, snapshotOnly })
                db.ArchivedConversations.Add(new ArchivedConversationSnapshot
                {
                    Id = conversation.Id, SessionId = session.Id, Generation = 1,
                    StudentId = conversation.StudentId,
                    Data = JsonSerializer.Serialize(conversation, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                });
            await db.SaveChangesAsync();
        }

        async Task AssertCountsAsync()
        {
            await using var scope = app.Services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<
                ICommandHandler<GetArchivedConversationCountsQuery, IReadOnlyDictionary<int, int>>>();
            var archives = scope.ServiceProvider.GetRequiredService<IArchivedConversationRepository>();
            var teacherCounts = await handler.ExecuteAsync(new(session.Id, [1, 2, 3], null), CancellationToken.None);
            Assert.Equal(2, teacherCounts.Count);
            Assert.Equal(5, teacherCounts[1]);
            Assert.Equal(1, teacherCounts[2]);
            foreach (var generation in new[] { 1, 2 })
                Assert.Equal((await archives.GetForGenerationAsync(session.Id, generation, null, CancellationToken.None)).Count,
                    teacherCounts[generation]);
            var studentCounts = await handler.ExecuteAsync(new(session.Id, [1, 2, 3], "completed"), CancellationToken.None);
            Assert.Equal(2, studentCounts.Count);
            Assert.All(studentCounts.Values, count => Assert.Equal(1, count));
            Assert.Equal(1, (await handler.ExecuteAsync(new(session.Id, [1], "reopened"), CancellationToken.None))[1]);
            Assert.Equal(1, (await handler.ExecuteAsync(new(session.Id, [1], "snapshot-only"), CancellationToken.None))[1]);
            Assert.Empty(await handler.ExecuteAsync(new(session.Id, [1, 2], "unrelated"), CancellationToken.None));
            Assert.Empty(await handler.ExecuteAsync(new(session.Id, [], null), CancellationToken.None));
        }
        await AssertCountsAsync();
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IArchivedConversationRepository>()
                .ArchiveQueueAsync(session.Id, 1, CancellationToken.None);
        await AssertCountsAsync();
    }
}
