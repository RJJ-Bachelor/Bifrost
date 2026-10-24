using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.Sessions.Application.Features.Teacher.Common;
using EirService.Sessions.Application.Features.Teacher.GetSessions;
using Microsoft.Extensions.DependencyInjection;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task TeacherSessionListCountsWaitingRequestsForOwnedAndJoinedSessions()
    {
        var empty = await CreateAsync("Empty session");
        var owned = await CreateAsync("Owned session");
        using var colleague = TeacherClient("colleague");
        using var sharedResponse = await colleague.PostAsJsonAsync("/api/teachers/sessions", new { Title = "Shared session" });
        Assert.Equal(HttpStatusCode.Created, sharedResponse.StatusCode);
        var shared = (await sharedResponse.Content.ReadFromJsonAsync<SessionResult>())!;
        using var hiddenResponse = await colleague.PostAsJsonAsync("/api/teachers/sessions", new { Title = "Hidden session" });
        Assert.Equal(HttpStatusCode.Created, hiddenResponse.StatusCode);
        var hidden = (await hiddenResponse.Content.ReadFromJsonAsync<SessionResult>())!;
        foreach (var code in new[] { owned.TeacherCode, shared.TeacherCode, shared.TeacherCode })
        {
            using var joined = await teacher.PostAsJsonAsync("/api/teachers/sessions/join", new { Code = code });
            Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        }

        var physical = QueueCountRequest(owned.Id, 1, HelpType.Physical);
        var digital = QueueCountRequest(owned.Id, 1);
        var faq = HelpRequest.CreateFaq(Guid.NewGuid(), owned.Id, 1, new MessageText("Generalized question"), clock.Now);
        var reserved = QueueCountRequest(owned.Id, 1, HelpType.Physical);
        reserved.Reserve(owned.CreatedByTeacherId, clock.Now);
        var active = QueueCountRequest(owned.Id, 1, HelpType.Physical);
        active.Reserve(owned.CreatedByTeacherId, clock.Now);
        active.Start(owned.CreatedByTeacherId, clock.Now);
        var awaitingFaq = QueueCountRequest(owned.Id, 1);
        awaitingFaq.LinkToFaq(Guid.NewGuid(), new MessageText("Generalized question"), clock.Now);
        var awaitingConfirmation = QueueCountRequest(owned.Id, 1, HelpType.Physical);
        var answeredFaqId = Guid.NewGuid();
        awaitingConfirmation.LinkToFaq(answeredFaqId, new MessageText("Generalized question"), clock.Now);
        awaitingConfirmation.SuggestFaq(answeredFaqId, owned.CreatedByTeacherId, new MessageText("Answer"), clock.Now);
        var completed = QueueCountRequest(owned.Id, 1);
        completed.Reserve(owned.CreatedByTeacherId, clock.Now);
        completed.Answer(owned.CreatedByTeacherId, new MessageText("Answer"), clock.Now);
        var cancelled = QueueCountRequest(owned.Id, 1);
        cancelled.Cancel(cancelled.StudentId!, AuthorRole.Student, clock.Now);
        await SeedQueueCountRequestsAsync(physical, digital, faq, reserved, active, awaitingFaq,
            awaitingConfirmation, completed, cancelled,
            QueueCountRequest(shared.Id, 1), QueueCountRequest(shared.Id, 1, HelpType.Physical),
            QueueCountRequest(hidden.Id, 1));

        var sessions = (await teacher.GetFromJsonAsync<SessionListResult[]>("/api/teachers/sessions"))!;
        Assert.Equal(new[] { empty.Id, owned.Id, shared.Id }.Order(), sessions.Select(session => session.Id).Order());
        Assert.Equal(0, sessions.Single(session => session.Id == empty.Id).WaitingCount);
        Assert.Equal(4, sessions.Single(session => session.Id == owned.Id).WaitingCount);
        Assert.Equal(2, sessions.Single(session => session.Id == shared.Id).WaitingCount);
        foreach (var session in sessions)
        {
            var queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
            Assert.Equal(queue.GetProperty("waitingCount").GetInt32(), session.WaitingCount);
        }
    }

    [PostgresFact]
    public async Task TeacherSessionListCountsUseEachSessionsCurrentGenerationAfterClearAndArchive()
    {
        var cleared = await CreateAsync("Cleared session");
        var archived = await CreateAsync("Archived session");
        await SeedQueueCountRequestsAsync(QueueCountRequest(cleared.Id, 1), QueueCountRequest(archived.Id, 1));
        using var firstClear = await ClearAsync(cleared.Id, 1);
        Assert.Equal(HttpStatusCode.OK, firstClear.StatusCode);
        using var secondClear = await ClearAsync(cleared.Id, 2);
        Assert.Equal(HttpStatusCode.OK, secondClear.StatusCode);
        using var archive = await ArchiveAsync(archived.Id, 1);
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);

        var emptyGenerations = (await teacher.GetFromJsonAsync<SessionListResult[]>("/api/teachers/sessions"))!;
        Assert.Equal(2, emptyGenerations.Length);
        Assert.All(emptyGenerations, session => Assert.Equal(0, session.WaitingCount));

        // Generation 2 is stale for one session and current for the other.
        await SeedQueueCountRequestsAsync(
            QueueCountRequest(cleared.Id, 2), QueueCountRequest(cleared.Id, 3),
            QueueCountRequest(archived.Id, 1),
            QueueCountRequest(archived.Id, 2), QueueCountRequest(archived.Id, 2, HelpType.Physical));
        var sessions = (await teacher.GetFromJsonAsync<SessionListResult[]>("/api/teachers/sessions"))!;
        Assert.Equal((3, 1), sessions.Where(session => session.Id == cleared.Id)
            .Select(session => (session.CurrentGeneration, session.WaitingCount)).Single());
        Assert.Equal((2, 2), sessions.Where(session => session.Id == archived.Id)
            .Select(session => (session.CurrentGeneration, session.WaitingCount)).Single());
        foreach (var session in sessions)
        {
            var queue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{session.Id}/queue");
            Assert.Equal(queue.GetProperty("waitingCount").GetInt32(), session.WaitingCount);
        }
    }

    private HelpRequest QueueCountRequest(Guid sessionId, int generation, HelpType helpType = HelpType.Digital) =>
        HelpRequest.Create(sessionId, generation, Guid.NewGuid().ToString("N"), helpType, new MessageText("Question"), clock.Now);

    private async Task SeedQueueCountRequestsAsync(params HelpRequest[] requests)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        foreach (var request in requests) request.ClearDomainEvents();
        db.Requests.AddRange(requests);
        await db.SaveChangesAsync();
    }
}
