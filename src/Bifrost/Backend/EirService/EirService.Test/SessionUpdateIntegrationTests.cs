using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.Contracts.Sessions.Common;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.KnowledgeBase.Domain.Entities;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using EirService.Participants.Infrastructure.Persistence;
using EirService.Sessions.Application.Features.Teacher.Common;
using EirService.Sessions.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task RenamePersistsOnlyTitlePreservesRelationshipsAndNotifiesCurrentParticipants()
    {
        var original = await CreateAsync("Original title");
        await JoinAsync(original.Code);
        using var colleague = await JoinTeacherAsync(original.TeacherCode);
        await SubmitAsync(student, original.Id, HelpType.Digital);
        clock.Now = clock.Now.AddHours(1);
        using (var archived = await ArchiveAsync(original.Id, 1))
            Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        clock.Now = clock.Now.AddHours(1);
        using (var cleared = await ClearAsync(original.Id, 2))
            Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        using var otherStudent = await OtherStudentAsync(original.Code);
        var requestId = await SubmitAsync(student, original.Id, HelpType.Digital);
        var otherRequestId = await SubmitAsync(otherStudent, original.Id, HelpType.Digital);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var faq = FaqAnswer.Create(Guid.NewGuid(), original.Id, 3, "Shared question",
                [requestId, otherRequestId], clock.Now);
            var knowledge = scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>();
            knowledge.Faqs.Add(faq);
            await knowledge.SaveChangesAsync();
        }
        var before = (await teacher.GetFromJsonAsync<SessionResult>($"/api/teachers/sessions/{original.Id}"))!;
        var relationships = await SnapshotRenameRelationshipsAsync(original.Id);
        var queue = await teacher.GetStringAsync($"/api/teachers/sessions/{original.Id}/queue");
        var privateRequest = await student.GetStringAsync($"/api/students/requests/{requestId}");
        DateTimeOffset generationStartedAt;
        int lifecycleEventCount;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
            generationStartedAt = (await db.Sessions.SingleAsync()).CurrentGenerationStartedAtUtc;
            lifecycleEventCount = await db.Outbox.CountAsync(message => message.RoutingKey == MessageRoutes.SessionUpdated);
        }
        clock.Now = clock.Now.AddHours(1);

        using var response = await colleague.PatchAsJsonAsync($"/api/teachers/sessions/{original.Id}", new
        {
            Title = "  New title  ", Code = "ZZZZZZZZ", TeacherCode = "YYYYYYYY",
            CurrentGeneration = 99, CreatedByTeacherId = "spoofed", LastClearedAtUtc = clock.Now
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var expected = before with { Title = "New title" };
        Assert.Equal(expected, await response.Content.ReadFromJsonAsync<SessionResult>());
        Assert.Equal(expected, await teacher.GetFromJsonAsync<SessionResult>($"/api/teachers/sessions/{original.Id}"));
        Assert.Equal(expected, Assert.Single((await teacher.GetFromJsonAsync<SessionResult[]>("/api/teachers/sessions"))!));
        var studentSession = (await student.GetFromJsonAsync<SessionSummary>($"/api/students/sessions/{original.Id}"))!;
        Assert.Equal((original.Id, "New title", 3), (studentSession.Id, studentSession.Title, studentSession.CurrentGeneration));
        Assert.Equal("New title", Assert.Single((await student.GetFromJsonAsync<SessionSummary[]>("/api/students/sessions"))!).Title);
        Assert.Equal(relationships, await SnapshotRenameRelationshipsAsync(original.Id));
        Assert.Equal(queue, await teacher.GetStringAsync($"/api/teachers/sessions/{original.Id}/queue"));
        Assert.Equal(privateRequest, await student.GetStringAsync($"/api/students/requests/{requestId}"));

        var studentId = (await student.GetFromJsonAsync<JsonElement>("/api/students/me")).GetProperty("userId").GetString()!;
        var otherStudentId = (await otherStudent.GetFromJsonAsync<JsonElement>("/api/students/me")).GetProperty("userId").GetString()!;
        await using var verification = app.Services.CreateAsyncScope();
        var saved = verification.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        Assert.Equal(generationStartedAt, (await saved.Sessions.SingleAsync()).CurrentGenerationStartedAtUtc);
        Assert.Equal(lifecycleEventCount, await saved.Outbox.CountAsync(message => message.RoutingKey == MessageRoutes.SessionUpdated));
        var notifications = (await saved.Outbox.Where(message => message.RoutingKey == MessageRoutes.NotificationCreated).ToArrayAsync())
            .Select(message => (message.MessageId, Payload: JsonSerializer.Deserialize<NotificationCreatedMessage>(message.Payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!))
            .Where(message => message.Payload.Change == "Renamed").ToArray();
        Assert.Equal(new[] { original.CreatedByTeacherId, "colleague", studentId, otherStudentId }.Order(),
            notifications.Select(message => message.Payload.UserId).Order());
        Assert.Equal(4, notifications.Select(message => message.MessageId).Distinct().Count());
        Assert.All(notifications, message =>
        {
            Assert.Equal(original.Id, message.Payload.SessionId);
            Assert.Equal(3, message.Payload.Generation);
            Assert.Equal(NotificationEventType.SessionChanged, message.Payload.EventType);
            Assert.Equal(NotificationDeliveryType.RealtimeOnly, message.Payload.DeliveryType);
            Assert.Null(message.Payload.HelpRequestId);
            Assert.Null(message.Payload.Status);
        });
    }

    [PostgresFact]
    public async Task RejectedRenameDoesNotChangeDatabaseOrQueueLifecycleAndAcceptsTrimmedTitleLimit()
    {
        var session = await CreateAsync();
        var path = $"/api/teachers/sessions/{session.Id}";
        using var anonymous = app.GetTestClient();
        using var anonymousResponse = await anonymous.PatchAsJsonAsync(path, new { Title = "New title" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        using var studentResponse = await student.PatchAsJsonAsync(path, new { Title = "New title" });
        Assert.Equal(HttpStatusCode.Unauthorized, studentResponse.StatusCode);
        using var outsider = TeacherClient("outsider");
        foreach (var id in new[] { session.Id, Guid.NewGuid() })
        {
            using var denied = await outsider.PatchAsJsonAsync($"/api/teachers/sessions/{id}",
                new { Title = "Unauthorized change", TeacherId = session.CreatedByTeacherId });
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
        foreach (var title in new[] { null, "", " \t ", new string('x', 201) })
        {
            using var invalid = await teacher.PatchAsJsonAsync(path, new { Title = title });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var missing = await teacher.PatchAsJsonAsync(path, new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
            Assert.Equal(session, SessionResult.From(await db.Sessions.SingleAsync()));
            Assert.Empty(await db.Outbox.ToArrayAsync());
        }

        using var maximum = await teacher.PatchAsJsonAsync(path, new { Title = " " + new string('x', 200) + " " });
        Assert.Equal(HttpStatusCode.OK, maximum.StatusCode);
        Assert.Equal(session with { Title = new string('x', 200) }, await maximum.Content.ReadFromJsonAsync<SessionResult>());
    }

    private async Task<string[]> SnapshotRenameRelationshipsAsync(Guid sessionId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var participants = scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>();
        var requests = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var knowledge = scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>();
        var sessions = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        return
        [
            JsonSerializer.Serialize(await participants.Participants.AsNoTracking().Where(item => item.SessionId == sessionId)
                .OrderBy(item => item.SubjectId).ThenBy(item => item.Role).ToArrayAsync()),
            JsonSerializer.Serialize(await requests.Requests.AsNoTracking().Include(item => item.Messages.OrderBy(message => message.SequenceNumber))
                .Where(item => item.SessionId == sessionId).OrderBy(item => item.Id).ToArrayAsync()),
            JsonSerializer.Serialize(await knowledge.Faqs.AsNoTracking().Where(item => item.SessionId == sessionId).OrderBy(item => item.Id).ToArrayAsync()),
            JsonSerializer.Serialize(await sessions.Archives.AsNoTracking().Where(item => item.SessionId == sessionId).OrderBy(item => item.Id).ToArrayAsync()),
            JsonSerializer.Serialize(await requests.ArchivedConversations.AsNoTracking().Where(item => item.SessionId == sessionId).OrderBy(item => item.Id).ToArrayAsync())
        ];
    }
}
