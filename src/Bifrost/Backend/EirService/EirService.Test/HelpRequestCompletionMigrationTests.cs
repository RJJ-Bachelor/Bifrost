using System.Text.Json;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.HelpRequests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task CompletionMigrationClassifiesLegacyRequestsArchivesAndUnpublishedMessages()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var sessionId = Guid.NewGuid();
        var now = clock.Now;
        HelpRequest Personal(string studentId, HelpType type) =>
            HelpRequest.Create(sessionId, 1, studentId, type, new MessageText("Question"), now);
        HelpRequest AcceptedFaq(string studentId, HelpType type)
        {
            var request = Personal(studentId, type);
            var faqId = Guid.NewGuid();
            request.LinkToFaq(faqId, new MessageText("Shared question"), now);
            request.SuggestFaq(faqId, "teacher", new MessageText("Shared answer"), now.AddMinutes(1));
            request.AcceptFaq(studentId, now.AddMinutes(2));
            return request;
        }
        var digital = Personal("digital", HelpType.Digital);
        digital.Reserve("teacher", now);
        digital.Answer("teacher", new MessageText("Answer"), now.AddMinutes(1));
        var physical = Personal("physical", HelpType.Physical);
        physical.Reserve("teacher", now);
        physical.Start("teacher", now.AddMinutes(1));
        physical.Complete("teacher", now.AddMinutes(2));
        var acceptedDigital = AcceptedFaq("faq-digital", HelpType.Digital);
        var acceptedPhysical = AcceptedFaq("faq-physical", HelpType.Physical);
        var faq = HelpRequest.CreateFaq(Guid.NewGuid(), sessionId, 1, new MessageText("Shared question"), now);
        faq.Reserve("teacher", now);
        faq.Answer("teacher", new MessageText("Shared answer"), now.AddMinutes(1));
        var waiting = Personal("waiting", HelpType.Digital);
        var cancelled = Personal("cancelled", HelpType.Physical);
        cancelled.Cancel("cancelled", AuthorRole.Student, now.AddMinutes(1));
        HelpRequest[] requests = [digital, physical, acceptedDigital, acceptedPhysical, faq, waiting, cancelled];
        db.Requests.AddRange(requests);

        var snapshots = requests.Where(request => !request.IsFaq).Select(request => new ArchivedConversationSnapshot
        {
            Id = Guid.NewGuid(), SessionId = sessionId, Generation = 1, StudentId = request.StudentId!,
            Data = JsonSerializer.Serialize(new
            {
                status = request.IsCompleted ? "Completed" : request.Status.ToString(),
                helpType = request.HelpType.ToString(), faqAnswerId = request.FaqAnswerId,
                conversation = "Preserve archived content"
            })
        }).ToArray();
        db.ArchivedConversations.AddRange(snapshots);

        OutboxMessage Update(HelpRequest request, string change, string route = MessageRoutes.HelpRequestUpdated)
        {
            var eventId = Guid.NewGuid();
            return OutboxMessage.Create(new OutgoingMessage(eventId,
                new HelpRequestUpdatedIntegrationEvent(eventId, request.Id, sessionId, 1,
                    request.StudentId, change, "Completed", request.HelpType.ToString(),
                    request.AssignedTeacherId, request.QueuedAt, now), MessageRoutes.Requests, route), now);
        }
        OutboxMessage Notice(HelpRequest request, string change) => OutboxMessage.Create(
            new OutgoingMessage(Guid.NewGuid(), new NotificationCreatedMessage(request.StudentId!, "Notice",
                sessionId, 1, request.Id, change, "Completed"), MessageRoutes.Notifications, MessageRoutes.NotificationCreated), now);
        var digitalUpdate = Update(digital, "Answered");
        var physicalUpdate = Update(physical, "Completed");
        var faqUpdate = Update(faq, "Answered");
        var acceptedUpdate = Update(acceptedPhysical, "FaqAccepted");
        var notices = new[] { Notice(digital, "Answered"), Notice(physical, "Completed"), Notice(acceptedDigital, "FaqAccepted") };
        var published = Notice(acceptedPhysical, "FaqAccepted");
        published.MarkPublished(now.AddMinutes(3));
        var unrelated = Update(digital, "Answered", "unrelated-route");
        // A delayed completion notification must use its own event, even after a follow-up.
        var reopened = AcceptedFaq("reopened", HelpType.Physical);
        var reopenedNotice = Notice(reopened, "FaqAccepted");
        reopened.AddStudentMessage("reopened", new MessageText("Follow-up"), now.AddMinutes(3));
        db.Requests.Add(reopened);
        db.Outbox.AddRange(new[] { digitalUpdate, physicalUpdate, faqUpdate, acceptedUpdate, published, unrelated, reopenedNotice }.Concat(notices));
        await db.SaveChangesAsync();

        static object State(HelpRequest request) => new
        {
            request.Id, request.Status, request.HelpType, request.FaqAnswerId, request.AssignedTeacherId,
            request.CreatedAt, request.QueuedAt, request.StartedAt, request.ClosedAt, request.RequestNumber
        };
        var before = await db.Requests.AsNoTracking().ToDictionaryAsync(request => request.Id, State);
        var messagesBefore = await db.Messages.AsNoTracking().OrderBy(message => message.Id)
            .Select(message => new { message.Id, message.HelpRequestId, message.Text, message.MessageType, message.SequenceNumber }).ToArrayAsync();
        await db.GetService<IMigrator>().MigrateAsync("20261023090000_ArchiveQueuedConversations");
        db.ChangeTracker.Clear();
        var legacyCount = await db.Database.SqlQuery<int>($"""
            SELECT COUNT(*)::int AS "Value" FROM helprequests."HelpRequest" WHERE "Status" = 'Completed'
            """).SingleAsync();
        Assert.Equal(5, legacyCount);

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        var after = await db.Requests.AsNoTracking().ToDictionaryAsync(request => request.Id);
        Assert.Equal(before.Count, after.Count);
        Assert.All(after.Values, request => Assert.Equal(before[request.Id], State(request)));
        Assert.Equal(HelpRequestStatus.CompletedByDigital, after[faq.Id].Status);
        Assert.Equal(HelpRequestStatus.CompletedByFaq, after[acceptedPhysical.Id].Status);
        Assert.Equal(HelpRequestStatus.Pending, after[reopened.Id].Status);
        var messagesAfter = await db.Messages.AsNoTracking().OrderBy(message => message.Id)
            .Select(message => new { message.Id, message.HelpRequestId, message.Text, message.MessageType, message.SequenceNumber }).ToArrayAsync();
        Assert.Equal(messagesBefore, messagesAfter);
        var archived = await db.ArchivedConversations.AsNoTracking().ToDictionaryAsync(snapshot => snapshot.StudentId);
        foreach (var request in requests.Where(request => !request.IsFaq))
        {
            using var data = JsonDocument.Parse(archived[request.StudentId!].Data);
            Assert.Equal(request.Status.ToString(), data.RootElement.GetProperty("status").GetString());
            Assert.Equal("Preserve archived content", data.RootElement.GetProperty("conversation").GetString());
        }
        var outbox = await db.Outbox.AsNoTracking().ToDictionaryAsync(message => message.Id);
        var expectedStatuses = new Dictionary<Guid, HelpRequestStatus>
        {
            [digitalUpdate.Id] = HelpRequestStatus.CompletedByDigital,
            [physicalUpdate.Id] = HelpRequestStatus.CompletedByPhysical,
            [faqUpdate.Id] = HelpRequestStatus.CompletedByDigital,
            [acceptedUpdate.Id] = HelpRequestStatus.CompletedByFaq,
            [notices[0].Id] = HelpRequestStatus.CompletedByDigital,
            [notices[1].Id] = HelpRequestStatus.CompletedByPhysical,
            [notices[2].Id] = HelpRequestStatus.CompletedByFaq,
            [reopenedNotice.Id] = HelpRequestStatus.CompletedByFaq
        };
        Assert.Equal(expectedStatuses.Count + 2, outbox.Count);
        foreach (var (id, status) in expectedStatuses)
        {
            using var data = JsonDocument.Parse(outbox[id].Payload);
            Assert.Equal(status.ToString(), data.RootElement.GetProperty("status").GetString());
            Assert.Null(outbox[id].PublishedAtUtc);
        }
        Assert.Equal(published.Payload, outbox[published.Id].Payload);
        Assert.Equal(published.PublishedAtUtc, outbox[published.Id].PublishedAtUtc);
        Assert.Equal(unrelated.Payload, outbox[unrelated.Id].Payload);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
