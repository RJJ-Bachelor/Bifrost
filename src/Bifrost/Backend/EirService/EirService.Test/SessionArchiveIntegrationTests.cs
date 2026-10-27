using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.Contracts.Exceptions;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.KnowledgeBase.Domain.Entities;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using EirService.Sessions.Application.Features.Common;
using EirService.Sessions.Application.Features.Teacher.ArchiveSession;
using EirService.Sessions.Application.Repositories;
using EirService.Sessions.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Persistence;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task ArchivePersistsMetadataAndOutboxAtomicallyAndAdvancesCurrentGeneration()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        clock.Now = clock.Now.AddHours(1);
        using var response = await ArchiveAsync(session.Id, 1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var archived = (await response.Content.ReadFromJsonAsync<ArchiveSessionResult>())!;
        Assert.Equal(session.Id, archived.SessionId);
        Assert.Equal(session.Title, archived.Title);
        Assert.Equal(1, archived.Generation);
        Assert.Equal(2, archived.CurrentGeneration);
        Assert.Equal(session.CreatedAtUtc, archived.StartedAtUtc);
        Assert.Equal(clock.Now, archived.ArchivedAtUtc);
        var list = (await teacher.GetFromJsonAsync<SessionArchiveSummary[]>($"/api/teachers/sessions/{session.Id}/archives"))!;
        Assert.Equal(archived.ArchiveId, Assert.Single(list).Id);
        Assert.Equal(session.CreatedAtUtc, list[0].StartedAtUtc);
        Assert.Equal(session.Title, list[0].Title);
        Assert.Equal(0, list[0].PrivateConversationCount);
        Assert.Equal(0, list[0].GeneralizedQuestionCount);
        var studentList = (await student.GetFromJsonAsync<SessionArchiveSummary[]>($"/api/students/sessions/{session.Id}/archives"))!;
        Assert.Equal(list, studentList);
        var detail = (await student.GetFromJsonAsync<SessionArchiveResult>(
            $"/api/students/sessions/{session.Id}/archives/{archived.ArchiveId}"))!;
        Assert.Empty(detail.Faqs);
        Assert.Empty(detail.Conversations);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        var saved = await db.Sessions.SingleAsync();
        Assert.Equal(2, saved.CurrentGeneration);
        Assert.Equal(clock.Now, saved.CurrentGenerationStartedAtUtc);
        Assert.Null(saved.LastClearedAtUtc);
        var message = await db.Outbox.SingleAsync(message => message.RoutingKey == MessageRoutes.SessionUpdated);
        Assert.Equal(MessageRoutes.SessionUpdated, message.RoutingKey);
        var update = JsonSerializer.Deserialize<SessionUpdatedIntegrationEvent>(message.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(SessionUpdateType.Archived, update.EventType);
        Assert.Equal(1, update.PreviousGeneration);
        Assert.Equal(2, update.Generation);
        Assert.Equal(message.MessageId, update.EventId);
        Assert.Equal(clock.Now, update.UpdatedAtUtc);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task ArchivesKeepIndividualTitlesAndGenerationBoundariesAcrossArchivesAndClear()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var expected = new List<ArchiveSessionResult>();

        clock.Now = clock.Now.AddHours(1);
        using (var response = await ArchiveAsync(session.Id, 1, "  First lesson  "))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            expected.Add((await response.Content.ReadFromJsonAsync<ArchiveSessionResult>())!);
        }
        clock.Now = clock.Now.AddHours(1);
        using (var response = await ArchiveAsync(session.Id, 2, "Second lesson"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            expected.Add((await response.Content.ReadFromJsonAsync<ArchiveSessionResult>())!);
        }
        clock.Now = clock.Now.AddHours(1);
        var clearedAt = clock.Now;
        using (var response = await ClearAsync(session.Id, 3))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        clock.Now = clock.Now.AddHours(1);
        using (var response = await ArchiveAsync(session.Id, 4, "After clear"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            expected.Add((await response.Content.ReadFromJsonAsync<ArchiveSessionResult>())!);
        }

        Assert.Equal(new[] { "First lesson", "Second lesson", "After clear" }, expected.Select(archive => archive.Title));
        Assert.Equal(new[] { 1, 2, 4 }, expected.Select(archive => archive.Generation));
        Assert.Equal(new[] { session.CreatedAtUtc, expected[0].ArchivedAtUtc, clearedAt },
            expected.Select(archive => archive.StartedAtUtc));
        Assert.Equal(new[] { session.CreatedAtUtc.AddHours(1), session.CreatedAtUtc.AddHours(2), clock.Now },
            expected.Select(archive => archive.ArchivedAtUtc));

        foreach (var (client, role) in new[] { (teacher, "teachers"), (student, "students") })
        {
            var archives = (await client.GetFromJsonAsync<SessionArchiveSummary[]>(
                $"/api/{role}/sessions/{session.Id}/archives"))!;
            Assert.Equal(expected.AsEnumerable().Reverse().Select(archive => archive.ArchiveId), archives.Select(archive => archive.Id));
            foreach (var archive in archives)
            {
                var original = expected.Single(item => item.ArchiveId == archive.Id);
                Assert.Equal((original.Title, original.Generation, original.StartedAtUtc, original.ArchivedAtUtc),
                    (archive.Title, archive.Generation, archive.StartedAtUtc, archive.ArchivedAtUtc));
                var detail = (await client.GetFromJsonAsync<SessionArchiveResult>(
                    $"/api/{role}/sessions/{session.Id}/archives/{archive.Id}"))!;
                Assert.Equal((archive.Id, archive.Title, archive.Generation, archive.StartedAtUtc, archive.ArchivedAtUtc),
                    (detail.Id, detail.Title, detail.Generation, detail.StartedAtUtc, detail.ArchivedAtUtc));
            }
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        var saved = await db.Sessions.AsNoTracking().SingleAsync();
        Assert.Equal(session.Title, saved.Title);
        Assert.Equal(5, saved.CurrentGeneration);
        Assert.Equal(clearedAt, saved.LastClearedAtUtc);
        Assert.Equal(clock.Now, saved.CurrentGenerationStartedAtUtc);
        Assert.Equal(3, await db.Archives.CountAsync());
    }

    [PostgresFact]
    public async Task ArchiveRejectsInvalidTitlesWithoutMutationAndAcceptsTrimmedLimitAndNullFallback()
    {
        var session = await CreateAsync();
        foreach (var title in new[] { "", " \t ", new string('x', 201) })
        {
            using var response = await ArchiveAsync(session.Id, 1, title);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
            var saved = await db.Sessions.AsNoTracking().SingleAsync();
            Assert.Equal(1, saved.CurrentGeneration);
            Assert.Equal(session.CreatedAtUtc, saved.CurrentGenerationStartedAtUtc);
            Assert.Empty(await db.Archives.ToArrayAsync());
            Assert.Empty(await db.Outbox.ToArrayAsync());
        }

        using var maximum = await ArchiveAsync(session.Id, 1, " " + new string('x', 200) + " ");
        Assert.Equal(HttpStatusCode.OK, maximum.StatusCode);
        Assert.Equal(new string('x', 200), (await maximum.Content.ReadFromJsonAsync<ArchiveSessionResult>())!.Title);
        using var fallback = await teacher.PostAsJsonAsync($"/api/teachers/sessions/{session.Id}/archive",
            new { ExpectedGeneration = 2, Title = (string?)null });
        Assert.Equal(HttpStatusCode.OK, fallback.StatusCode);
        Assert.Equal(session.Title, (await fallback.Content.ReadFromJsonAsync<ArchiveSessionResult>())!.Title);
    }

    [PostgresFact]
    public async Task ConcurrentArchiveAndRetryCreateOneArchiveAndOneEvent()
    {
        var session = await CreateAsync();
        var responses = await Task.WhenAll(ArchiveAsync(session.Id, 1), ArchiveAsync(session.Id, 1));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        using var retry = await ArchiveAsync(session.Id, 1);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        Assert.Equal(2, (await db.Sessions.SingleAsync()).CurrentGeneration);
        Assert.Equal(1, await db.Archives.CountAsync());
        Assert.Equal(1, await db.Outbox.CountAsync(message => message.RoutingKey == MessageRoutes.SessionUpdated));
    }

    [PostgresFact]
    public async Task ArchiveAccessUsesAuthenticatedRoleAndMembershipAndBindsIdToSession()
    {
        var session = await CreateAsync();
        using var anonymous = app.GetTestClient();
        using var unauthenticated = await anonymous.PostAsJsonAsync($"/api/teachers/sessions/{session.Id}/archive", new { ExpectedGeneration = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using var studentWrite = await student.PostAsJsonAsync($"/api/teachers/sessions/{session.Id}/archive", new { ExpectedGeneration = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, studentWrite.StatusCode);
        using var notJoined = await student.GetAsync($"/api/students/sessions/{session.Id}/archives");
        Assert.Equal(HttpStatusCode.NotFound, notJoined.StatusCode);
        await JoinAsync(session.Code);
        using var otherTeacher = app.GetTestClient();
        otherTeacher.DefaultRequestHeaders.Add("X-Test-Role", "Teacher");
        otherTeacher.DefaultRequestHeaders.Add("X-Test-Scope", "Bifrost");
        otherTeacher.DefaultRequestHeaders.Add("X-Test-Subject", "colleague");
        using var forbidden = await otherTeacher.PostAsJsonAsync($"/api/teachers/sessions/{session.Id}/archive",
            new { ExpectedGeneration = 1, TeacherId = "provider|opaque-teacher" });
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        using var joined = await otherTeacher.PostAsJsonAsync("/api/teachers/sessions/join", new { Code = session.TeacherCode });
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        using var response = await otherTeacher.PostAsJsonAsync($"/api/teachers/sessions/{session.Id}/archive", new { ExpectedGeneration = 1 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var archiveId = (await response.Content.ReadFromJsonAsync<ArchiveSessionResult>())!.ArchiveId;
        var another = await CreateAsync("Other");
        using var wrongSession = await teacher.GetAsync($"/api/teachers/sessions/{another.Id}/archives/{archiveId}");
        Assert.Equal(HttpStatusCode.NotFound, wrongSession.StatusCode);
        using var missing = await student.GetAsync($"/api/students/sessions/{session.Id}/archives/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var invalid = await teacher.PostAsJsonAsync($"/api/teachers/sessions/{session.Id}/archive", new { ExpectedGeneration = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [PostgresFact]
    public async Task ArchivePreservesFaqsAndPrivateReopenedConversationsWhileDeletingQueue()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var other = await OtherStudentAsync(session.Code);
        var mineId = await SubmitAsync(student, session.Id, HelpType.Digital);
        var otherId = await SubmitAsync(other, session.Id, HelpType.Digital);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            foreach (var request in await db.Requests.Include(request => request.Messages)
                .Where(request => request.Id == mineId || request.Id == otherId).ToArrayAsync())
            {
                request.Reserve(session.CreatedByTeacherId, clock.Now);
                request.Answer(session.CreatedByTeacherId, new MessageText("Private answer"), clock.Now);
                if (request.Id == mineId) request.AddStudentMessage(request.StudentId!, new MessageText("Follow-up"), clock.Now);
                request.ClearDomainEvents();
            }
            await db.SaveChangesAsync();
            var knowledge = scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>();
            var answered = FaqAnswer.Create(Guid.NewGuid(), session.Id, 1, "Shared question", [mineId, otherId], clock.Now);
            answered.RecordAnswer("Shared answer", session.CreatedByTeacherId, clock.Now);
            var unanswered = FaqAnswer.Create(Guid.NewGuid(), session.Id, 1, "Unanswered question", [mineId, otherId], clock.Now);
            knowledge.Faqs.AddRange(answered, unanswered);
            await knowledge.SaveChangesAsync();
        }
        using var archivedResponse = await ArchiveAsync(session.Id, 1);
        var archived = (await archivedResponse.Content.ReadFromJsonAsync<ArchiveSessionResult>())!;
        var archiveUrl = $"/api/teachers/sessions/{session.Id}/archives/{archived.ArchiveId}";
        var before = (await teacher.GetFromJsonAsync<SessionArchiveResult>(archiveUrl))!;
        Assert.Equal(2, before.Faqs.Count);
        Assert.Equal(2, before.Conversations.Count);
        Assert.Equal(3, before.Conversations.Single(conversation => conversation.Id == mineId).Messages.Count);
        var beforeSummary = Assert.Single((await teacher.GetFromJsonAsync<SessionArchiveSummary[]>(
            $"/api/teachers/sessions/{session.Id}/archives"))!);
        Assert.Equal(2, beforeSummary.PrivateConversationCount);
        Assert.Equal(2, beforeSummary.GeneralizedQuestionCount);
        var studentBeforeSummary = Assert.Single((await student.GetFromJsonAsync<SessionArchiveSummary[]>(
            $"/api/students/sessions/{session.Id}/archives"))!);
        Assert.Equal(1, studentBeforeSummary.PrivateConversationCount);
        Assert.Equal(1, studentBeforeSummary.GeneralizedQuestionCount);
        // Logical queue empties before RabbitMQ delivery, and the next generation is independent.
        var queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
        Assert.Empty(queue.GetProperty("requests").EnumerateArray());
        var newId = await SubmitAsync(student, session.Id, HelpType.Digital);
        using (var scope = app.Services.CreateScope())
        {
            var update = new SessionUpdatedIntegrationEvent(Guid.NewGuid(), session.Id, 2,
                SessionUpdateType.Archived, clock.Now, 1);
            var handler = scope.ServiceProvider.GetRequiredService<EirService.HelpRequests.Application.Features.Internal.SessionUpdated.ISessionUpdatedMessageHandler>();
            await handler.HandleAsync(new(update.EventId, update), CancellationToken.None);
            await handler.HandleAsync(new(update.EventId, update), CancellationToken.None);
        }
        var after = (await teacher.GetFromJsonAsync<SessionArchiveResult>(archiveUrl))!;
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        Assert.Equal(beforeSummary, Assert.Single((await teacher.GetFromJsonAsync<SessionArchiveSummary[]>(
            $"/api/teachers/sessions/{session.Id}/archives"))!));
        Assert.Equal(studentBeforeSummary, Assert.Single((await student.GetFromJsonAsync<SessionArchiveSummary[]>(
            $"/api/students/sessions/{session.Id}/archives"))!));
        var own = (await student.GetFromJsonAsync<SessionArchiveResult>(
            $"/api/students/sessions/{session.Id}/archives/{archived.ArchiveId}"))!;
        Assert.Single(own.Faqs);
        Assert.Equal(mineId, Assert.Single(own.Conversations).Id);
        using var verifyScope = app.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        Assert.False(await verify.Requests.AnyAsync(request => request.Id == mineId));
        Assert.True(await verify.Requests.AnyAsync(request => request.Id == otherId));
        Assert.True(await verify.Requests.AnyAsync(request => request.Id == newId));
        Assert.Equal(1, await verify.ArchivedConversations.CountAsync());
    }

    [PostgresFact]
    public async Task InFlightOldGenerationWriteIsRejectedEvenWhenSessionWasAlreadyTracked()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var studentId = (await student.GetFromJsonAsync<EirService.Api.Endpoints.Student.Participants.GetStudentIdentity.StudentIdentityResponse>("/api/students/me"))!.UserId;
        using var staleScope = app.Services.CreateScope();
        var staleSession = await staleScope.ServiceProvider.GetRequiredService<ISessionRepository>().GetAsync(session.Id, CancellationToken.None);
        Assert.Equal(1, staleSession!.CurrentGeneration);
        var request = HelpRequest.Create(session.Id, 1, studentId, HelpType.Digital, new MessageText("Delayed"), clock.Now);
        await staleScope.ServiceProvider.GetRequiredService<IHelpRequestRepository>().AddAsync(request, CancellationToken.None);
        using var response = await ArchiveAsync(session.Id, 1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Assert.ThrowsAsync<OperationConflictException>(() =>
            staleScope.ServiceProvider.GetRequiredService<IUnitOfWork<IHelpRequestRepository>>().SaveChangesAsync());
        using var verifyScope = app.Services.CreateScope();
        Assert.False(await verifyScope.ServiceProvider.GetRequiredService<EirRequestDbContext>()
            .Requests.AnyAsync(saved => saved.Id == request.Id));
    }

    [PostgresFact]
    public async Task ArchiveMigrationBackfillsStartTimeForExistingSessions()
    {
        var initial = await CreateAsync("Initial");
        var cleared = await CreateAsync("Previously cleared");
        clock.Now = clock.Now.AddHours(1);
        using var clear = await ClearAsync(cleared.Id, 1);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261020074835_AddTeacherSessionCode");
        await db.Database.MigrateAsync();
        var sessions = await db.Sessions.AsNoTracking().ToArrayAsync();
        Assert.Equal(initial.CreatedAtUtc, sessions.Single(session => session.Id == initial.Id).CurrentGenerationStartedAtUtc);
        Assert.Equal(clock.Now, sessions.Single(session => session.Id == cleared.Id).CurrentGenerationStartedAtUtc);
        Assert.Empty(await db.Archives.ToArrayAsync());
    }

    private Task<HttpResponseMessage> ArchiveAsync(Guid id, int generation, string? title = null) =>
        title is null
            ? teacher.PostAsJsonAsync($"/api/teachers/sessions/{id}/archive", new { ExpectedGeneration = generation })
            : teacher.PostAsJsonAsync($"/api/teachers/sessions/{id}/archive", new { ExpectedGeneration = generation, Title = title });
}
