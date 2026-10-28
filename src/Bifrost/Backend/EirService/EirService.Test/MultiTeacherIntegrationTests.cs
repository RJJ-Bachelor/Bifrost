using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.Contracts.Exceptions;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.Participants.Domain.Entities;
using EirService.Participants.Infrastructure.Persistence;
using EirService.Sessions.Application.Features.Teacher.Common;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Persistence;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task TeacherJoinsConcurrentlyOnceAndCanUseSessionAfterClear()
    {
        var session = await CreateAsync();
        using var colleague = TeacherClient("colleague");
        var joins = await Task.WhenAll(
            colleague.PostAsJsonAsync("/api/teachers/sessions/join", new { Code = session.TeacherCode, TeacherId = "spoofed" }),
            colleague.PostAsJsonAsync("/api/teachers/sessions/join", new { Code = session.TeacherCode }));
        foreach (var response in joins)
        {
            using (response) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal(session, Assert.Single((await colleague.GetFromJsonAsync<SessionResult[]>("/api/teachers/sessions"))!));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>();
        var membership = await db.Participants.SingleAsync();
        Assert.Equal("colleague", membership.SubjectId);
        Assert.Equal(ParticipantRole.Teacher, membership.Role);
        Assert.Equal(clock.Now, membership.JoinedAtUtc);
        using var deleted = await colleague.DeleteAsync($"/api/teachers/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        using var clear = await colleague.PostAsJsonAsync($"/api/teachers/sessions/{session.Id}/clear", new { ExpectedGeneration = 1 });
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Equal(2, (await colleague.GetFromJsonAsync<SessionResult>($"/api/teachers/sessions/{session.Id}"))!.CurrentGeneration);
        using var ownerDelete = await teacher.DeleteAsync($"/api/teachers/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.NoContent, ownerDelete.StatusCode);
        Assert.Empty((await colleague.GetFromJsonAsync<SessionResult[]>("/api/teachers/sessions"))!);
        using var missing = await colleague.GetAsync($"/api/teachers/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [PostgresFact]
    public async Task TwoTeachersCompetingForOneRequestCommitOnlyOneReservation()
    {
        var session = await CreateAsync();
        using var colleague = await JoinTeacherAsync(session.TeacherCode);
        await JoinAsync(session.Code);
        var id = await SubmitAsync(student, session.Id, HelpType.Digital);
        var responses = await Task.WhenAll(
            teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null),
            colleague.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null));
        var winners = new List<string>();
        foreach (var response in responses)
        {
            using (response)
            {
                Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NoContent });
                if (response.StatusCode != HttpStatusCode.OK) continue;
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(id, result.GetProperty("id").GetGuid());
                winners.Add(result.GetProperty("assignedTeacherId").GetString()!);
            }
        }
        var winner = Assert.Single(winners);
        var loserClient = winner == "colleague" ? teacher : colleague;
        using var answer = await loserClient.PostAsJsonAsync($"/api/teachers/requests/{id}/messages", new { Text = "Stolen answer" });
        Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
        using var returned = await loserClient.PatchAsJsonAsync($"/api/teachers/requests/{id}/status", new { Action = "Cancel" });
        Assert.Equal(HttpStatusCode.Conflict, returned.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        Assert.Equal(winner, (await db.Requests.SingleAsync(request => request.Id == id)).AssignedTeacherId);
        var changes = await db.Outbox.Where(message => message.RoutingKey == MessageRoutes.HelpRequestUpdated).ToArrayAsync();
        Assert.Single(changes, message => JsonSerializer.Deserialize<HelpRequestUpdatedIntegrationEvent>(message.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Change == "Reserved");
    }

    [PostgresFact]
    public async Task ConcurrentTakeNextForSameTeacherReturnsOneReservationWithoutRestartingItsDuration()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var otherStudent = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Digital);
        clock.Now = clock.Now.AddMinutes(1);
        var secondId = await SubmitAsync(otherStudent, session.Id, HelpType.Digital);
        var reservedAt = clock.Now;

        var responses = await Task.WhenAll(
            teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null),
            teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null));
        foreach (var response in responses)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var request = (await response.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!;
                Assert.Equal(firstId, request.Id);
                Assert.Equal("provider|opaque-teacher", request.AssignedTeacherId);
                Assert.Equal(reservedAt, request.StartedAt);
                Assert.Equal("Question", Assert.Single(request.Messages).Text);
            }
        }

        clock.Now = clock.Now.AddMinutes(1);
        using var retry = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var retried = (await retry.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!;
        Assert.Equal(firstId, retried.Id);
        Assert.Equal(reservedAt, retried.StartedAt);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        Assert.Equal(1, await db.Requests.CountAsync(request => request.AssignedTeacherId == "provider|opaque-teacher"));
        Assert.Null((await db.Requests.SingleAsync(request => request.Id == secondId)).AssignedTeacherId);
        var changes = await db.Outbox.Where(message => message.RoutingKey == MessageRoutes.HelpRequestUpdated).ToArrayAsync();
        Assert.Single(changes, message => JsonSerializer.Deserialize<HelpRequestUpdatedIntegrationEvent>(message.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Change == "Reserved");
    }

    [PostgresFact]
    public async Task TeacherResumesPendingAndActivePhysicalHelpBeforeTakingAnotherRequest()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var otherStudent = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Physical);
        clock.Now = clock.Now.AddMinutes(1);
        var secondId = await SubmitAsync(otherStudent, session.Id, HelpType.Physical);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
            Assert.Equal(HttpStatusCode.OK, take.StatusCode);
            var request = (await take.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!;
            Assert.Equal(firstId, request.Id);
            Assert.Equal(HelpRequestStatus.Pending, request.Status);
            Assert.Null(request.StartedAt);
        }

        using var start = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{firstId}/status", new { Action = "Start" });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        using var resume = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
        var active = (await resume.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!;
        Assert.Equal(firstId, active.Id);
        Assert.Equal(HelpRequestStatus.Active, active.Status);
        Assert.Equal("Question", Assert.Single(active.Messages).Text);
        await AssertQueueCountsAsync(session.Id, waiting: 1, unreserved: 1);

        clock.Now = clock.Now.AddSeconds(30);
        using var complete = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{firstId}/status", new { Action = "Complete" });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        using var next = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Equal(secondId, (await next.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Id);
        await AssertQueueCountsAsync(session.Id, waiting: 1, unreserved: 0);
    }

    [PostgresFact]
    public async Task LegacyMultipleAssignmentsResumeActiveHelpThenOldestReservation()
    {
        var session = await CreateAsync();
        var teacherId = "provider|opaque-teacher";
        var older = HelpRequest.Create(session.Id, 1, "older", HelpType.Digital, new MessageText("Older question"), clock.Now.AddMinutes(-10));
        var newer = HelpRequest.Create(session.Id, 1, "newer", HelpType.Digital, new MessageText("Newer question"), clock.Now.AddMinutes(-5));
        var active = HelpRequest.Create(session.Id, 1, "active", HelpType.Physical, new MessageText("Active question"), clock.Now);
        var waiting = HelpRequest.Create(session.Id, 1, "waiting", HelpType.Digital, new MessageText("Waiting question"), clock.Now.AddMinutes(-20));
        older.Reserve(teacherId, clock.Now);
        newer.Reserve(teacherId, clock.Now);
        active.Reserve(teacherId, clock.Now);
        active.Start(teacherId, clock.Now);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            db.Requests.AddRange(older, newer, active, waiting);
            await db.SaveChangesAsync();
        }

        using var resumeActive = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, resumeActive.StatusCode);
        Assert.Equal(active.Id, (await resumeActive.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Id);
        using var complete = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{active.Id}/status", new { Action = "Complete" });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        using var resumeOlder = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, resumeOlder.StatusCode);
        Assert.Equal(older.Id, (await resumeOlder.Content.ReadFromJsonAsync<HelpRequestResult>(FaqJsonOptions))!.Id);
        await using var verification = app.Services.CreateAsyncScope();
        Assert.Null((await verification.ServiceProvider.GetRequiredService<EirRequestDbContext>().Requests
            .SingleAsync(request => request.Id == waiting.Id)).AssignedTeacherId);
    }

    [PostgresFact]
    public async Task TwoTeachersTakeDifferentRequestsAndCannotChangeEachOthersAssignment()
    {
        var session = await CreateAsync();
        using var colleague = await JoinTeacherAsync(session.TeacherCode);
        await JoinAsync(session.Code);
        using var otherStudent = await OtherStudentAsync(session.Code);
        var ids = new[] { await SubmitAsync(student, session.Id, HelpType.Physical),
            await SubmitAsync(otherStudent, session.Id, HelpType.Physical) };
        var responses = await Task.WhenAll(
            teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null),
            colleague.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null));
        var taken = new List<Guid>();
        for (var i = 0; i < responses.Length; i++)
        {
            using var response = responses[i];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            var id = result.GetProperty("id").GetGuid();
            taken.Add(id);
            Assert.Equal(i == 0 ? "provider|opaque-teacher" : "colleague", result.GetProperty("assignedTeacherId").GetString());
            var own = i == 0 ? teacher : colleague;
            var other = i == 0 ? colleague : teacher;
            using var forbidden = await other.PatchAsJsonAsync($"/api/teachers/requests/{id}/status", new { Action = "Start" });
            Assert.Equal(HttpStatusCode.Conflict, forbidden.StatusCode);
            using var started = await own.PatchAsJsonAsync($"/api/teachers/requests/{id}/status", new { Action = "Start" });
            Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        }
        Assert.Equal(ids.Order(), taken.Order());
    }

    [PostgresFact]
    public async Task FailedReservationOutboxInsertRollsBackAssignmentAndAllowsRetry()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var id = await SubmitAsync(student, session.Id, HelpType.Digital);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE helprequests.outbox ADD CONSTRAINT "RejectReservationForTest"
                CHECK ("Payload"::jsonb->>'change' <> 'Reserved');
                """);
            var repository = scope.ServiceProvider.GetRequiredService<IHelpRequestRepository>();
            await Assert.ThrowsAsync<DbUpdateException>(() => repository.TakeNextAsync(session.Id, 1, "provider|opaque-teacher", default));
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE helprequests.outbox DROP CONSTRAINT "RejectReservationForTest";
                """);
        }
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            var saved = await db.Requests.SingleAsync(request => request.Id == id);
            Assert.Null(saved.AssignedTeacherId);
            Assert.Equal(HelpRequestStatus.Pending, saved.Status);
            Assert.Equal(4, await db.Outbox.CountAsync()); // Created, request text, owner notice and student realtime update.
        }
        using var retry = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(id, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

    [PostgresFact]
    public async Task StaleRequestUpdateCannotOverwriteAnotherTeachersReservation()
    {
        var session = await CreateAsync();
        using var colleague = await JoinTeacherAsync(session.TeacherCode);
        await JoinAsync(session.Code);
        var id = await SubmitAsync(student, session.Id, HelpType.Digital);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var stale = await db.Requests.SingleAsync(request => request.Id == id);
        using var reserved = await colleague.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, reserved.StatusCode);
        stale.Reserve("provider|opaque-teacher", clock.Now);
        await Assert.ThrowsAsync<OperationConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<IUnitOfWork<IHelpRequestRepository>>().SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal("colleague", (await db.Requests.SingleAsync(request => request.Id == id)).AssignedTeacherId);
        Assert.Equal(2, await db.Outbox.CountAsync(message => message.RoutingKey == MessageRoutes.HelpRequestUpdated));
    }

    [PostgresFact]
    public async Task EmptyQueueNotifiesAllTeachersWithIndependentDeliveryIds()
    {
        var session = await CreateAsync();
        using var colleague = await JoinTeacherAsync(session.TeacherCode);
        using var ownerJoin = await teacher.PostAsJsonAsync("/api/teachers/sessions/join", new { Code = session.TeacherCode });
        Assert.Equal(HttpStatusCode.OK, ownerJoin.StatusCode);
        await JoinAsync(session.Code);
        using var other = await OtherStudentAsync(session.Code);
        await Task.WhenAll(SubmitAsync(student, session.Id, HelpType.Digital), SubmitAsync(other, session.Id, HelpType.Digital));
        var notifications = await ReadNotificationsAsync();
        Assert.Equal(new[] { "colleague", "provider|opaque-teacher" }, notifications.Select(item => item.Message.UserId).Order());
        Assert.Equal(2, notifications.Select(item => item.Id).Distinct().Count());
        Assert.Single(notifications.Select(item => item.Message.HelpRequestId).Distinct());
        Assert.All(notifications, item => Assert.Equal(Shared.Domain.Notifications.NotificationEventType.HelpRequestCreated, item.Message.EventType));
    }

    [PostgresFact]
    public async Task ParticipantRoleMigrationPreservesStudentsAndSupportsBothRolesForSameSubject()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261011085409_InitialParticipants");
        var sessionId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO participants.session_participants ("SessionId", "StudentSubjectId", "JoinedAtUtc")
            VALUES ({sessionId}, 'same-subject', {clock.Now});
            """);
        await db.Database.MigrateAsync();
        var studentMembership = await db.Participants.SingleAsync();
        Assert.Equal("same-subject", studentMembership.SubjectId);
        Assert.Equal(ParticipantRole.Student, studentMembership.Role);
        Assert.Equal(clock.Now, studentMembership.JoinedAtUtc);
        db.Participants.Add(SessionParticipant.Join(sessionId, "same-subject", ParticipantRole.Teacher, clock.Now));
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Participants.CountAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        await migrator.MigrateAsync("20261011085409_InitialParticipants");
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(ParticipantRole.Student, (await db.Participants.SingleAsync()).Role);
    }

    private HttpClient TeacherClient(string subject)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Teacher");
        client.DefaultRequestHeaders.Add("X-Test-Scope", "Bifrost");
        client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
        return client;
    }

    private async Task<HttpClient> JoinTeacherAsync(string code)
    {
        var client = TeacherClient("colleague");
        using var response = await client.PostAsJsonAsync("/api/teachers/sessions/join", new { Code = code });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
