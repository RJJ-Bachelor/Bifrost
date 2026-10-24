using System.Net;
using EirService.Contracts.Exceptions;
using Shared.Application.Persistence;
using System.Text.Json;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.HelpRequests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task ArchivingDeletesTheOpenQueueAndPreservesReopenedPrivateConversationsIdempotently()
    {
        var session = await CreateAsync();
        var now = clock.Now;
        HelpRequest Personal(string studentId, int generation = 1, Guid? sessionId = null) =>
            HelpRequest.Create(sessionId ?? session.Id, generation, studentId, HelpType.Digital, new MessageText("Question"), now);
        var waiting = Personal("waiting");
        var reopened = Personal("reopened");
        reopened.Reserve("teacher", now);
        reopened.Answer("teacher", new MessageText("Private answer"), now);
        reopened.AddStudentMessage("reopened", new MessageText("Follow-up"), now.AddMinutes(1));
        var completed = Personal("completed");
        completed.Reserve("teacher", now);
        completed.Answer("teacher", new MessageText("Completed answer"), now);
        var cancelled = Personal("cancelled");
        cancelled.Cancel("cancelled", AuthorRole.Student, now);
        var faq = HelpRequest.CreateFaq(Guid.NewGuid(), session.Id, 1, new MessageText("Shared question"), now);
        var awaitingFaq = Personal("faq-student");
        awaitingFaq.LinkToFaq(faq.Id, new MessageText("Shared question"), now);
        awaitingFaq.SuggestFaq(faq.Id, "teacher", new MessageText("Shared answer"), now);
        var active = HelpRequest.Create(session.Id, 1, "active", HelpType.Physical, new MessageText("Physical help"), now);
        active.Reserve("teacher", now);
        active.Start("teacher", now);
        var nextGeneration = Personal("next-generation", 2);
        var otherSession = Personal("other-session", sessionId: Guid.NewGuid());
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            db.Requests.AddRange(waiting, reopened, completed, cancelled, faq, awaitingFaq, active, nextGeneration, otherSession);
            await db.SaveChangesAsync();
        }

        string before;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var archives = scope.ServiceProvider.GetRequiredService<IArchivedConversationRepository>();
            var conversations = await archives.GetForGenerationAsync(session.Id, 1, null, CancellationToken.None);
            Assert.Equal(new[] { reopened.Id, completed.Id, cancelled.Id, awaitingFaq.Id }.Order(), conversations.Select(item => item.Id).Order());
            before = JsonSerializer.Serialize(conversations);
            Assert.Equal(reopened.Id, Assert.Single(await archives.GetForGenerationAsync(session.Id, 1, "reopened", CancellationToken.None)).Id);
            Assert.Empty(await archives.GetForGenerationAsync(session.Id, 1, "unrelated", CancellationToken.None));
            await archives.ArchiveQueueAsync(session.Id, 1, CancellationToken.None);
        }
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var archives = scope.ServiceProvider.GetRequiredService<IArchivedConversationRepository>();
            Assert.Equal(before, JsonSerializer.Serialize(await archives.GetForGenerationAsync(session.Id, 1, null, CancellationToken.None)));
            await archives.ArchiveQueueAsync(session.Id, 1, CancellationToken.None);
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            Assert.Equal(new[] { completed.Id, cancelled.Id, nextGeneration.Id, otherSession.Id }.Order(),
                (await db.Requests.Where(request => request.SessionId == session.Id || request.Id == otherSession.Id).Select(request => request.Id).ToArrayAsync()).Order());
            Assert.Equal(new[] { reopened.Id, awaitingFaq.Id }.Order(),
                (await db.ArchivedConversations.Select(snapshot => snapshot.Id).ToArrayAsync()).Order());
            Assert.False(await db.Messages.AnyAsync(message => message.HelpRequestId == reopened.Id ||
                message.HelpRequestId == waiting.Id || message.HelpRequestId == faq.Id));
            var privateConversation = Assert.Single(await archives.GetForGenerationAsync(session.Id, 1, "reopened", CancellationToken.None));
            Assert.Equal(new[] { "Question", "Private answer", "Follow-up" }, privateConversation.Messages.Select(message => message.Text));
        }
    }

    [PostgresFact]
    public async Task InFlightFollowUpCannotChangeAnArchivedConversation()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var requestId = await SubmitAsync(student, session.Id, HelpType.Digital);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            var request = await db.Requests.Include(request => request.Messages).SingleAsync(request => request.Id == requestId);
            request.Reserve("teacher", clock.Now);
            request.Answer("teacher", new MessageText("Archived answer"), clock.Now);
            request.ClearDomainEvents();
            await db.SaveChangesAsync();
        }
        await using var stale = app.Services.CreateAsyncScope();
        var staleRequest = (await stale.ServiceProvider.GetRequiredService<IHelpRequestRepository>()
            .GetAsync(requestId, CancellationToken.None))!;
        staleRequest.AddStudentMessage(staleRequest.StudentId!, new MessageText("Delayed follow-up"), clock.Now);
        using var archived = await ArchiveAsync(session.Id, 1);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        await Assert.ThrowsAsync<OperationConflictException>(() => stale.ServiceProvider
            .GetRequiredService<IUnitOfWork<IHelpRequestRepository>>().SaveChangesAsync());
        await using var verify = app.Services.CreateAsyncScope();
        var stored = await verify.ServiceProvider.GetRequiredService<EirRequestDbContext>().Requests
            .Include(request => request.Messages).SingleAsync(request => request.Id == requestId);
        Assert.Equal(HelpRequestStatus.Completed, stored.Status);
        Assert.Equal(new[] { "Question", "Archived answer" }, stored.Messages.OrderBy(message => message.SequenceNumber)
            .Select(message => message.Text.Value));
    }
}
