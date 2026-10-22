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
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task RejectedFaqMigrationHidesOnlyDocumentedRejectedEpisodesAndPreservesTheQueueAndSharedFaqs()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var sessionId = Guid.NewGuid();
        var now = clock.Now;
        var faqIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var rejectedMessageIds = new HashSet<Guid>();
        var updates = new List<OutboxMessage>();

        HelpRequest Source(string studentId) => HelpRequest.Create(sessionId, 1, studentId,
            HelpType.Digital, new MessageText("The student's original question"), now);

        void Suggest(HelpRequest source, int faqIndex, int minute)
        {
            source.LinkToFaq(faqIds[faqIndex], new MessageText($"Shared question {faqIndex}"), now.AddMinutes(minute));
            source.SuggestFaq(faqIds[faqIndex], "teacher", new MessageText($"Shared answer {faqIndex}"), now.AddMinutes(minute + 1));
        }

        OutboxMessage Update(HelpRequest source, DateTimeOffset occurredAt, string route = MessageRoutes.HelpRequestUpdated)
        {
            var eventId = Guid.NewGuid();
            var payload = new HelpRequestUpdatedIntegrationEvent(eventId, source.Id, sessionId, 1,
                source.StudentId, "FaqRejected", source.Status.ToString(), source.HelpType.ToString(),
                source.AssignedTeacherId, source.QueuedAt, occurredAt);
            return OutboxMessage.Create(new OutgoingMessage(eventId, payload, MessageRoutes.Requests, route), occurredAt);
        }

        void Reject(HelpRequest source, int minute, bool published)
        {
            var latestQuestion = source.Messages.Where(message => message.MessageType == HelpRequestMessageType.FaqQuestion)
                .Max(message => message.SequenceNumber);
            rejectedMessageIds.UnionWith(source.Messages.Where(message => message.SequenceNumber >= latestQuestion)
                .Select(message => message.Id));
            source.RejectFaq(source.StudentId!, now.AddMinutes(minute));
            var update = Update(source, now.AddMinutes(minute));
            if (published) update.MarkPublished(now.AddMinutes(minute + 1));
            updates.Add(update);
        }

        var rejected = Source("rejected-source");
        Suggest(rejected, 0, 1);
        Reject(rejected, 3, published: true);
        rejected.Reserve("individual-teacher", now.AddMinutes(4));

        var legacyPhysicalRejected = Source("legacy-physical-rejected-source");
        Suggest(legacyPhysicalRejected, 0, 1);
        Reject(legacyPhysicalRejected, 3, published: true);

        var acceptedFollowUp = Source("accepted-follow-up-source");
        Suggest(acceptedFollowUp, 0, 1);
        acceptedFollowUp.AcceptFaq(acceptedFollowUp.StudentId!, now.AddMinutes(3));
        acceptedFollowUp.AddStudentMessage(acceptedFollowUp.StudentId!, new MessageText("A new follow-up"), now.AddMinutes(4));

        var repeated = Source("repeated-source");
        Suggest(repeated, 0, 1);
        Reject(repeated, 3, published: false);
        Suggest(repeated, 1, 11);
        Reject(repeated, 13, published: true);
        Suggest(repeated, 2, 21);
        repeated.AcceptFaq(repeated.StudentId!, now.AddMinutes(23));

        var rejectedThenAcceptedFollowUp = Source("rejected-then-accepted-follow-up-source");
        Suggest(rejectedThenAcceptedFollowUp, 0, 1);
        Reject(rejectedThenAcceptedFollowUp, 3, published: false);
        Suggest(rejectedThenAcceptedFollowUp, 1, 11);
        rejectedThenAcceptedFollowUp.AcceptFaq(rejectedThenAcceptedFollowUp.StudentId!, now.AddMinutes(13));
        rejectedThenAcceptedFollowUp.AddStudentMessage(rejectedThenAcceptedFollowUp.StudentId!,
            new MessageText("A follow-up after an accepted answer"), now.AddMinutes(14));

        var ordinary = Source("ordinary-source");
        ordinary.Reserve("teacher", now.AddMinutes(1));
        ordinary.Answer("teacher", new MessageText("An individual answer"), now.AddMinutes(2));

        var shared = faqIds.Select((id, index) =>
        {
            var faq = HelpRequest.CreateFaq(id, sessionId, 1, new MessageText($"Shared question {index}"), now);
            faq.Reserve("teacher", now.AddMinutes(1));
            faq.Answer("teacher", new MessageText($"Shared answer {index}"), now.AddMinutes(2));
            return faq;
        }).ToArray();

        // An unrelated route must not turn an accepted FAQ into a rejected one.
        updates.Add(Update(acceptedFollowUp, now.AddMinutes(5), MessageRoutes.HelpRequestCreated));
        var sources = new[] { rejected, legacyPhysicalRejected, acceptedFollowUp, repeated, rejectedThenAcceptedFollowUp, ordinary };
        db.Requests.AddRange(sources.Concat(shared));
        db.Outbox.AddRange(updates);
        await db.SaveChangesAsync();

        // Recreate the old representation: the decision existed in the outbox, but
        // every conversation message was returned, including rejected FAQ episodes.
        await db.Database.ExecuteSqlRawAsync("UPDATE helprequests.\"HelpRequestMessage\" SET \"IsRejected\" = FALSE;");
        // Older versions also allowed physical requests to join and reject FAQs.
        // Seed that legacy state without relaxing the current domain rules.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE helprequests."HelpRequest" SET "HelpType" = 'Physical'
            WHERE "Id" = {legacyPhysicalRejected.Id};
            """);
        db.ChangeTracker.Clear();

        static object QueueState(HelpRequest request) => new
        {
            request.Id, request.SessionId, request.Generation, request.StudentId, request.HelpType,
            request.Status, request.FaqAnswerId, request.AssignedTeacherId, request.RequestNumber,
            request.CreatedAt, request.QueuedAt, request.ReturnedToQueueAt, request.StartedAt, request.ClosedAt
        };

        var requestsBefore = await db.Requests.AsNoTracking().ToDictionaryAsync(request => request.Id);
        var historyBefore = await db.Messages.AsNoTracking().OrderBy(message => message.Id)
            .Select(message => new { message.Id, message.HelpRequestId, message.AuthorId, message.AuthorRole,
                message.MessageType, message.Text, message.CreatedAt, message.SequenceNumber }).ToArrayAsync();
        Assert.All(await db.Messages.AsNoTracking().ToArrayAsync(), message => Assert.False(message.IsRejected));

        await db.GetService<IMigrator>().MigrateAsync("20261022200000_KeepPhysicalHelpRequestsInQueue");
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        var requestsAfter = await db.Requests.AsNoTracking().Include(request => request.Messages)
            .ToDictionaryAsync(request => request.Id);
        Assert.Equal(requestsBefore.Count, requestsAfter.Count);
        Assert.All(requestsAfter.Values, request => Assert.Equal(QueueState(requestsBefore[request.Id]), QueueState(request)));
        var historyAfter = await db.Messages.AsNoTracking().OrderBy(message => message.Id)
            .Select(message => new { message.Id, message.HelpRequestId, message.AuthorId, message.AuthorRole,
                message.MessageType, message.Text, message.CreatedAt, message.SequenceNumber }).ToArrayAsync();
        Assert.Equal(historyBefore, historyAfter);
        Assert.Equal(rejectedMessageIds.Order(), requestsAfter.Values.SelectMany(request => request.Messages)
            .Where(message => message.IsRejected).Select(message => message.Id).Order());

        var visibleRejected = requestsAfter[rejected.Id].ToResult().Messages;
        Assert.Equal(HelpRequestMessageType.StudentQuestion, Assert.Single(visibleRejected).MessageType);
        var persistedPhysical = requestsAfter[legacyPhysicalRejected.Id];
        Assert.Equal(HelpType.Physical, persistedPhysical.HelpType);
        Assert.Equal(HelpRequestStatus.Pending, persistedPhysical.Status);
        Assert.Equal(HelpRequestMessageType.StudentQuestion, Assert.Single(persistedPhysical.ToResult().Messages).MessageType);
        foreach (var source in new[] { acceptedFollowUp, repeated, rejectedThenAcceptedFollowUp })
        {
            var persisted = requestsAfter[source.Id];
            Assert.Equal(new[] { HelpRequestMessageType.FaqQuestion, HelpRequestMessageType.FaqAnswer },
                persisted.ToResult().Messages.Where(message => message.MessageType is HelpRequestMessageType.FaqQuestion
                    or HelpRequestMessageType.FaqAnswer).Select(message => message.MessageType));
        }
        foreach (var faq in shared)
        {
            var persisted = requestsAfter[faq.Id];
            Assert.All(persisted.Messages, message => Assert.False(message.IsRejected));
            Assert.Equal(new[] { HelpRequestMessageType.FaqQuestion, HelpRequestMessageType.FaqAnswer },
                persisted.ToResult().Messages.Select(message => message.MessageType));
        }
        Assert.False(requestsAfter[acceptedFollowUp.Id].CanLinkToFaq);
        Assert.False(requestsAfter[rejectedThenAcceptedFollowUp.Id].CanLinkToFaq);
        var outboxAfter = await db.Outbox.AsNoTracking().ToDictionaryAsync(message => message.Id);
        Assert.Equal(updates.Count, outboxAfter.Count);
        foreach (var update in updates)
        {
            Assert.Equal(update.Payload, outboxAfter[update.Id].Payload);
            Assert.Equal(update.PublishedAtUtc, outboxAfter[update.Id].PublishedAtUtc);
        }
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
