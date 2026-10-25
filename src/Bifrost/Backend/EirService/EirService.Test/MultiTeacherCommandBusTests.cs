using System.Net;
using Shared.Domain.Notifications;
using System.Net.Http.Json;
using EirService.Contracts.Exceptions;
using EirService.Contracts.Sessions.GetHelpRequestSession;
using EirService.HelpRequests.Application.Features.Student.CreateHelpRequest;
using EirService.HelpRequests.Domain.Entities;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Application.Features.Teacher.JoinSession;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using EirService.Sessions.Application.Features.Teacher.Common;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using EirService.Sessions.Application.Features.Teacher.GetSession;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace EirService.Test;

public sealed partial class CommandBusTests
{
    [Fact]
    public async Task JoinChecksTheCodeForItsRoleBeforeCreatingMembership()
    {
        var session = await new CreateSessionCommand(TeacherId: "owner", Title: "Shared").ExecuteAsync();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            new JoinTeacherSessionCommand(session.Code, "colleague").ExecuteAsync());
        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            new JoinSessionCommand(session.TeacherCode, "student").ExecuteAsync());
        var participants = _app.Services.GetRequiredService<IParticipantRepository>();
        Assert.Empty(await participants.GetSessionIdsAsync("colleague", ParticipantRole.Teacher, default));
        Assert.Empty(await participants.GetSessionIdsAsync("student", ParticipantRole.Student, default));
    }

    [Fact]
    public async Task TeacherJoinUsesClaimsAndListsBothOwnedAndJoinedSessionsOnce()
    {
        var shared = await new CreateSessionCommand(TeacherId: "owner", Title: "Shared").ExecuteAsync();
        var owned = await new CreateSessionCommand(TeacherId: "colleague", Title: "Owned").ExecuteAsync();
        Authenticate("Teacher", "colleague");
        using var denied = await _client.GetAsync($"/api/teachers/sessions/{shared.Id}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var join = await _client.PostAsJsonAsync("/api/teachers/sessions/join",
                new { Code = shared.TeacherCode, TeacherId = "spoofed" });
            Assert.Equal(HttpStatusCode.OK, join.StatusCode);
            Assert.Equal(shared, await join.Content.ReadFromJsonAsync<SessionResult>());
        }
        await new JoinTeacherSessionCommand(owned.TeacherCode, "colleague").ExecuteAsync();
        var listed = (await _client.GetFromJsonAsync<SessionResult[]>("/api/teachers/sessions"))!;
        Assert.Equal(new[] { shared.Id, owned.Id }.Order(), listed.Select(session => session.Id).Order());
        Assert.Equal(shared, await _client.GetFromJsonAsync<SessionResult>($"/api/teachers/sessions/{shared.Id}"));
        var access = (await new GetHelpRequestSessionQuery(shared.Id).ExecuteAsync())!;
        Assert.Equal(new[] { "owner", "colleague" }, access.TeacherIds);
        Assert.DoesNotContain("spoofed", access.TeacherIds);
    }

    [Fact]
    public async Task StudentMembershipNeverGrantsTeacherAccessAndRolesCanCoexist()
    {
        var session = await new CreateSessionCommand(TeacherId: "owner", Title: "Shared").ExecuteAsync();
        await new JoinSessionCommand(session.Code, "same-subject").ExecuteAsync();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            new GetSessionQuery(session.Id, "same-subject").ExecuteAsync());
        await new JoinTeacherSessionCommand(session.TeacherCode, "same-subject").ExecuteAsync();
        Assert.Equal(session, await new GetSessionQuery(session.Id, "same-subject").ExecuteAsync());
        var participants = _app.Services.GetRequiredService<IParticipantRepository>();
        Assert.True(await participants.IsParticipantAsync(session.Id, "same-subject", ParticipantRole.Student, default));
        Assert.True(await participants.IsParticipantAsync(session.Id, "same-subject", ParticipantRole.Teacher, default));
        Assert.False(await participants.IsParticipantAsync(session.Id, "owner", ParticipantRole.Student, default));
    }

    [Fact]
    public async Task EmptyQueueNotifiesEveryTeacherOnceWithDistinctMessageIds()
    {
        var session = await new CreateSessionCommand(TeacherId: "owner", Title: "Shared").ExecuteAsync();
        await new JoinTeacherSessionCommand(session.TeacherCode, "colleague").ExecuteAsync();
        await new JoinTeacherSessionCommand(session.TeacherCode, "owner").ExecuteAsync();
        await new JoinSessionCommand(session.Code, "student").ExecuteAsync();
        var request = await new CreateHelpRequestCommand(session.Id, "student", "Help", HelpType.Digital).ExecuteAsync();

        Assert.Equal(new[] { "colleague", "owner", "student" }, _requests.Notifications.Select(item => item.Message.UserId).Order());
        Assert.Equal(3, _requests.Notifications.Select(item => item.Id).Distinct().Count());
        var direct = _requests.Notifications.Where(item => item.Message.DeliveryType == NotificationDeliveryType.RealtimeOrPush).ToArray();
        Assert.Equal(new[] { "colleague", "owner" }, direct.Select(item => item.Message.UserId).Order());
        Assert.All(direct, item => Assert.Equal(request.Id, item.Message.HelpRequestId));
        var generic = Assert.Single(_requests.Notifications, item => item.Message.UserId == "student").Message;
        Assert.Equal(NotificationDeliveryType.RealtimeOnly, generic.DeliveryType);
        Assert.Equal(NotificationEventType.SessionChanged, generic.EventType);
        Assert.Equal(request.Id, generic.HelpRequestId);
        Assert.Equal("Pending", generic.Status);
    }

    [Theory]
    [InlineData(null, "Bifrost", HttpStatusCode.Unauthorized)]
    [InlineData("Student", "Bifrost", HttpStatusCode.Forbidden)]
    [InlineData("Teacher", "wrong-scope", HttpStatusCode.Forbidden)]
    public async Task TeacherJoinRequiresAuthenticatedTeacherWithScope(string? role, string scope, HttpStatusCode expected)
    {
        if (role is not null) Authenticate(role, "caller", scope);
        using var response = await _client.PostAsJsonAsync("/api/teachers/sessions/join", new { Code = "ABCD2345" });
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task TeacherJoinValidatesCodeAndDoesNotCreateMembershipForMissingSession()
    {
        Authenticate("Teacher", "caller");
        using var invalid = await _client.PostAsJsonAsync("/api/teachers/sessions/join", new { Code = " " });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var missing = await _client.PostAsJsonAsync("/api/teachers/sessions/join", new { Code = "ABCD2345" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<SessionResult[]>("/api/teachers/sessions"))!);
    }
}
