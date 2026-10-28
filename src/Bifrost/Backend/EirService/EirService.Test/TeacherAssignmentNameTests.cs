using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.Contracts.Sessions.GetSessionTeacherNames;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Application.Features.Teacher.JoinSession;
using EirService.Participants.Infrastructure.Persistence;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using FullHelpRequestResult = EirService.HelpRequests.Application.Features.Common.HelpRequestResult;
using ListHelpRequestResult = EirService.HelpRequests.Application.Features.Student.GetHelpRequests.HelpRequestResult;
using TeacherQueueResult = EirService.HelpRequests.Application.Features.Teacher.GetQueue.TeacherQueueResult;

namespace EirService.Test;

public sealed partial class CommandBusTests
{
    private static readonly JsonSerializerOptions TeacherAssignmentJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StudentReadsIncludeAssignedTeacherNameForCreatorAndJoinedTeacher(bool assignedToCreator)
    {
        var studentId = AuthenticateStudent();
        var session = await new CreateSessionCommand("owner", "Shared", "Owner Name").ExecuteAsync();
        await new JoinSessionCommand(session.Code, studentId).ExecuteAsync();
        await new JoinTeacherSessionCommand(session.TeacherCode, "colleague", "Henrik").ExecuteAsync();
        var request = HelpRequest.Create(session.Id, 1, studentId, HelpType.Physical, new MessageText("Help"), DateTimeOffset.UtcNow);
        request.Reserve(assignedToCreator ? "owner" : "colleague", DateTimeOffset.UtcNow);
        _requests.Saved.Add(request);

        var detail = (await _client.GetFromJsonAsync<FullHelpRequestResult>($"/api/students/requests/{request.Id}", TeacherAssignmentJsonOptions))!;
        var listed = Assert.Single((await _client.GetFromJsonAsync<ListHelpRequestResult[]>("/api/students/requests"))!);
        var expected = assignedToCreator ? "Owner Name" : "Henrik";
        Assert.Equal(request.AssignedTeacherId, detail.AssignedTeacherId);
        Assert.Equal(expected, detail.AssignedTeacherName);
        Assert.Equal(request.AssignedTeacherId, listed.AssignedTeacherId);
        Assert.Equal(expected, listed.AssignedTeacherName);
    }

    [Fact]
    public async Task TeacherQueueIncludesNamesForPendingAndActiveAssignmentsAndKeepsOwnReservationAndCounts()
    {
        var session = await new CreateSessionCommand("owner", "Named queue", "Owner Name").ExecuteAsync();
        await new JoinTeacherSessionCommand(session.TeacherCode, "colleague", "Henrik").ExecuteAsync();
        var now = DateTimeOffset.UtcNow;
        var pending = HelpRequest.Create(session.Id, 1, "pending-student", HelpType.Digital, new MessageText("Digital help"), now);
        pending.Reserve("colleague", now);
        var active = HelpRequest.Create(session.Id, 1, "active-student", HelpType.Physical, new MessageText("Physical help"), now);
        active.Reserve("owner", now);
        active.Start("owner", now);
        var waiting = HelpRequest.Create(session.Id, 1, "waiting-student", HelpType.Digital, new MessageText("Waiting help"), now);
        _requests.Saved.AddRange([pending, active, waiting]);
        Authenticate("Teacher", "owner");

        var queue = (await _client.GetFromJsonAsync<TeacherQueueResult>(
            $"/api/teachers/sessions/{session.Id}/queue", TeacherAssignmentJsonOptions))!;

        Assert.Equal(2, queue.WaitingCount);
        Assert.Equal(1, queue.UnreservedWaitingCount);
        Assert.Equal(active.Id, queue.MyReservedRequestId);
        Assert.Equal("Henrik", Assert.Single(queue.Requests, request => request.Id == pending.Id).AssignedTeacherName);
        Assert.Equal("Owner Name", Assert.Single(queue.ActiveRequests).AssignedTeacherName);
        Assert.Null(Assert.Single(queue.Requests, request => request.Id == waiting.Id).AssignedTeacherName);
    }

    [Fact]
    public async Task TeacherJoinUsesTrustedNameAndMissingNameOnRejoinPreservesIt()
    {
        var studentId = AuthenticateStudent();
        var session = await new CreateSessionCommand("owner", "Shared").ExecuteAsync();
        await new JoinSessionCommand(session.Code, studentId).ExecuteAsync();
        Authenticate("Teacher", "colleague");
        _client.DefaultRequestHeaders.Add("X-Test-Name", "Henrik");
        using var joined = await _client.PostAsJsonAsync("/api/teachers/sessions/join",
            new { Code = session.TeacherCode, TeacherId = "spoofed", DisplayName = "Spoofed Name" });
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        _client.DefaultRequestHeaders.Remove("X-Test-Name");
        using var rejoined = await _client.PostAsJsonAsync("/api/teachers/sessions/join", new { Code = session.TeacherCode });
        Assert.Equal(HttpStatusCode.OK, rejoined.StatusCode);
        var request = HelpRequest.Create(session.Id, 1, studentId, HelpType.Physical, new MessageText("Help"), DateTimeOffset.UtcNow);
        request.Reserve("colleague", DateTimeOffset.UtcNow);
        _requests.Saved.Add(request);

        var detail = (await _client.GetFromJsonAsync<FullHelpRequestResult>($"/api/students/requests/{request.Id}", TeacherAssignmentJsonOptions))!;
        Assert.Equal("colleague", detail.AssignedTeacherId);
        Assert.Equal("Henrik", detail.AssignedTeacherName);
        var names = await new GetSessionTeacherNamesQuery([session.Id]).ExecuteAsync();
        Assert.False(names[session.Id].ContainsKey("spoofed"));
    }

    [Fact]
    public async Task TeacherNamesStayScopedToEachSessionAndUnknownNamesRemainNull()
    {
        var studentId = AuthenticateStudent();
        var first = await new CreateSessionCommand("owner", "First").ExecuteAsync();
        var second = await new CreateSessionCommand("owner", "Second").ExecuteAsync();
        var third = await new CreateSessionCommand("owner", "Third").ExecuteAsync();
        foreach (var session in new[] { first, second, third })
        {
            await new JoinSessionCommand(session.Code, studentId).ExecuteAsync();
            await new JoinTeacherSessionCommand(session.TeacherCode, "colleague", session.Id == first.Id ? "Henrik" : null).ExecuteAsync();
            var request = HelpRequest.Create(session.Id, 1, studentId, HelpType.Physical, new MessageText("Help"), DateTimeOffset.UtcNow);
            if (session.Id != third.Id) request.Reserve("colleague", DateTimeOffset.UtcNow);
            _requests.Saved.Add(request);
        }

        var listed = (await _client.GetFromJsonAsync<ListHelpRequestResult[]>("/api/students/requests"))!;
        Assert.Equal("Henrik", listed.Single(request => request.SessionId == first.Id).AssignedTeacherName);
        var unknown = listed.Single(request => request.SessionId == second.Id);
        Assert.Equal("colleague", unknown.AssignedTeacherId);
        Assert.Null(unknown.AssignedTeacherName);
        var unassigned = listed.Single(request => request.SessionId == third.Id);
        Assert.Null(unassigned.AssignedTeacherId);
        Assert.Null(unassigned.AssignedTeacherName);
    }
}

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task TakingRequestRefreshesExistingTeacherMembershipFromTrustedName()
    {
        var session = await CreateAsync();
        using var colleague = await JoinTeacherAsync(session.TeacherCode);
        await JoinAsync(session.Code);
        var id = await SubmitAsync(student, session.Id, HelpType.Physical);
        colleague.DefaultRequestHeaders.Add("X-Test-Name", "Henrik");
        using var taken = await colleague.PostAsJsonAsync($"/api/teachers/sessions/{session.Id}/queue/take-next",
            new { DisplayName = "Spoofed Name", TeacherId = "spoofed" });
        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
        var result = await taken.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Henrik", result.GetProperty("assignedTeacherName").GetString());
        var detail = await student.GetFromJsonAsync<JsonElement>($"/api/students/requests/{id}");
        Assert.Equal("colleague", detail.GetProperty("assignedTeacherId").GetString());
        Assert.Equal("Henrik", detail.GetProperty("assignedTeacherName").GetString());
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>();
        var member = await db.Participants.SingleAsync(participant => participant.SubjectId == "colleague");
        Assert.Equal("Henrik", member.DisplayName);
        Assert.Equal(clock.Now, member.JoinedAtUtc);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}

public sealed class TeacherParticipantDisplayNameMigrationTests
{
    [Fact]
    public void ParticipantDisplayNameMigrationMatchesCurrentModelAndAddsNullableName()
    {
        using var db = new EirParticipantDbContext(new DbContextOptionsBuilder<EirParticipantDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var script = db.GetService<IMigrator>()
            .GenerateScript("20261020072325_AddParticipantRoles", "20261027120000_AddParticipantDisplayNames");
        Assert.Contains("ALTER TABLE participants.session_participants ADD \"DisplayName\" text;", script);
    }
}
