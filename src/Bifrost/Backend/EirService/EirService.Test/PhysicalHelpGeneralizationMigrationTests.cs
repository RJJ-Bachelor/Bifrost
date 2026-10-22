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
using Shared.Domain.Notifications;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task PhysicalHelpMigrationRestoresWaitingHelpAndSuppressesOnlyUnpublishedPhysicalFaqNotices()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var sessionId = Guid.NewGuid();
        var faqId = Guid.NewGuid();
        var now = clock.Now;

        HelpRequest Waiting(string studentId)
        {
            // Build the history allowed by the old version, then set its physical type in SQL.
            var request = HelpRequest.Create(sessionId, 1, studentId, HelpType.Digital, new MessageText("Original question"), now);
            request.Reserve("previous-teacher", now.AddMinutes(1));
            request.Cancel("previous-teacher", AuthorRole.Teacher, now.AddMinutes(2));
            request.LinkToFaq(faqId, new MessageText("Shared question"), now.AddMinutes(3));
            return request;
        }

        var awaitingQuestion = Waiting("physical-awaiting-question");
        var awaitingAnswer = Waiting("physical-awaiting-answer");
        awaitingAnswer.SuggestFaq(faqId, "teacher", new MessageText("Shared answer"), now.AddMinutes(4));
        var digital = Waiting("digital-awaiting-answer");
        digital.SuggestFaq(faqId, "teacher", new MessageText("Shared answer"), now.AddMinutes(4));
        var completed = Waiting("physical-completed");
        completed.SuggestFaq(faqId, "teacher", new MessageText("Shared answer"), now.AddMinutes(4));
        completed.AcceptFaq(completed.StudentId!, now.AddMinutes(5));
        var cancelled = Waiting("physical-cancelled");
        cancelled.Cancel(cancelled.StudentId!, AuthorRole.Student, now.AddMinutes(4));
        var reserved = HelpRequest.Create(sessionId, 1, "physical-reserved", HelpType.Physical, new MessageText("Physical question"), now);
        reserved.Reserve("teacher", now.AddMinutes(1));
        var active = HelpRequest.Create(sessionId, 1, "physical-active", HelpType.Physical, new MessageText("Physical question"), now);
        active.Reserve("teacher", now.AddMinutes(1));
        active.Start("teacher", now.AddMinutes(2));
        var ordinary = HelpRequest.Create(sessionId, 1, "ordinary-digital", HelpType.Digital, new MessageText("Unrelated question"), now);
        var faq = HelpRequest.CreateFaq(faqId, sessionId, 1, new MessageText("Shared question"), now.AddMinutes(3));
        db.Requests.AddRange(awaitingQuestion, awaitingAnswer, digital, completed, cancelled, reserved, active, ordinary, faq);

        OutboxMessage Notice(HelpRequest request, string change, NotificationEventType eventType = NotificationEventType.Unknown,
            string? recipient = null, string? route = null)
        {
            var payload = new NotificationCreatedMessage(recipient ?? request.StudentId!, "Notice", sessionId, 1,
                request.Id, change, request.Status.ToString(), eventType);
            return OutboxMessage.Create(new OutgoingMessage(Guid.NewGuid(), payload,
                MessageRoutes.Notifications, route ?? MessageRoutes.NotificationCreated), now.AddMinutes(5));
        }

        var legacyFaqNotice = Notice(awaitingAnswer, "FaqSuggested");
        var typedFaqNotice = Notice(awaitingQuestion, "Legacy", NotificationEventType.FaqAnswered);
        var closedPhysicalFaqNotice = Notice(completed, "FaqSuggested", NotificationEventType.FaqAnswered);
        var publishedFaqNotice = Notice(awaitingAnswer, "FaqSuggested", NotificationEventType.FaqAnswered);
        publishedFaqNotice.MarkPublished(now.AddMinutes(6));
        var digitalFaqNotice = Notice(digital, "FaqSuggested", NotificationEventType.FaqAnswered);
        var teacherNotice = Notice(faq, "FaqCreated", NotificationEventType.FaqCreated, "teacher");
        var physicalStartNotice = Notice(active, "Started", NotificationEventType.HelpRequestStarted);
        var otherRecipientNotice = Notice(awaitingAnswer, "FaqSuggested", NotificationEventType.FaqAnswered, "teacher");
        var otherRouteNotice = Notice(awaitingAnswer, "FaqSuggested", NotificationEventType.FaqAnswered, route: "unrelated-route");
        db.Outbox.AddRange(legacyFaqNotice, typedFaqNotice, closedPhysicalFaqNotice, publishedFaqNotice,
            digitalFaqNotice, teacherNotice, physicalStartNotice, otherRecipientNotice, otherRouteNotice);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE helprequests."HelpRequest" SET "HelpType" = 'Physical'
            WHERE "Id" IN ({awaitingQuestion.Id}, {awaitingAnswer.Id}, {completed.Id}, {cancelled.Id});
            """);
        db.ChangeTracker.Clear();

        static object Snapshot(HelpRequest request) => new
        {
            request.Id, request.SessionId, request.Generation, request.StudentId, request.HelpType, request.Status,
            request.FaqAnswerId, request.AssignedTeacherId, request.CreatedAt, request.QueuedAt,
            request.ReturnedToQueueAt, request.StartedAt, request.ClosedAt, request.RequestNumber
        };

        var before = await db.Requests.AsNoTracking().ToDictionaryAsync(request => request.Id);
        var historyBefore = await db.Messages.AsNoTracking().OrderBy(message => message.Id)
            .Select(message => new { message.Id, message.HelpRequestId, message.AuthorId, message.AuthorRole,
                message.MessageType, message.Text, message.CreatedAt, message.SequenceNumber }).ToArrayAsync();

        await db.GetService<IMigrator>().MigrateAsync("20261022145938_AddHelpRequestMessageType");
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        var after = await db.Requests.AsNoTracking().ToDictionaryAsync(request => request.Id);
        foreach (var id in new[] { awaitingQuestion.Id, awaitingAnswer.Id })
        {
            var request = after[id];
            Assert.Equal(HelpType.Physical, request.HelpType);
            Assert.Equal(HelpRequestStatus.Pending, request.Status);
            Assert.Null(request.FaqAnswerId);
            Assert.Null(request.AssignedTeacherId);
            Assert.Null(request.StartedAt);
            Assert.Null(request.ClosedAt);
            Assert.Equal(before[id].CreatedAt, request.CreatedAt);
            Assert.Equal(before[id].QueuedAt, request.QueuedAt);
            Assert.Equal(before[id].ReturnedToQueueAt, request.ReturnedToQueueAt);
            Assert.Equal(before[id].RequestNumber, request.RequestNumber);
            Assert.True(request.IsOpen);
        }
        foreach (var id in new[] { digital.Id, completed.Id, cancelled.Id, reserved.Id, active.Id, ordinary.Id, faq.Id })
            Assert.Equal(Snapshot(before[id]), Snapshot(after[id]));
        var historyAfter = await db.Messages.AsNoTracking().OrderBy(message => message.Id)
            .Select(message => new { message.Id, message.HelpRequestId, message.AuthorId, message.AuthorRole,
                message.MessageType, message.Text, message.CreatedAt, message.SequenceNumber }).ToArrayAsync();
        Assert.Equal(historyBefore, historyAfter);

        var remaining = await db.Outbox.AsNoTracking().ToDictionaryAsync(message => message.Id);
        Assert.DoesNotContain(legacyFaqNotice.Id, remaining.Keys);
        Assert.DoesNotContain(typedFaqNotice.Id, remaining.Keys);
        Assert.DoesNotContain(closedPhysicalFaqNotice.Id, remaining.Keys);
        Assert.Equal(6, remaining.Count);
        Assert.Equal(publishedFaqNotice.PublishedAtUtc, remaining[publishedFaqNotice.Id].PublishedAtUtc);
        foreach (var retained in new[] { digitalFaqNotice, teacherNotice, physicalStartNotice, otherRecipientNotice, otherRouteNotice })
        {
            Assert.Equal(retained.Payload, remaining[retained.Id].Payload);
            Assert.Null(remaining[retained.Id].PublishedAtUtc);
        }
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
