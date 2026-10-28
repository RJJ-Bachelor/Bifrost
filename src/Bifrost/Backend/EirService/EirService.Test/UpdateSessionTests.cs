using System.Net;
using System.Net.Http.Json;
using EirService.Contracts.Sessions.Common;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Application.Features.Teacher.JoinSession;
using EirService.Sessions.Application.Features.Teacher.Common;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using EirService.Sessions.Application.Features.Teacher.GetSession;
using EirService.Sessions.Application.Features.Teacher.UpdateSession;
using EirService.Sessions.Domain.DomainEvents;
using EirService.Sessions.Domain.Entities;
using EirService.Sessions.Domain.ValueObjects;
using FastEndpoints;

namespace EirService.Test;

public sealed class UpdateSessionTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" \t\n ", false)]
    [InlineData("A", true)]
    [InlineData("  Algorithms  ", true)]
    public void TitleMustContainText(string? title, bool valid)
    {
        var command = new UpdateSessionCommand(Guid.NewGuid(), "teacher", title!);

        Assert.Equal(valid, new UpdateSessionCommandValidator().Validate(command).IsValid);
    }

    [Fact]
    public void ValidatorRequiresSessionAndTeacherAndChecksTitleLengthAfterTrimming()
    {
        var validator = new UpdateSessionCommandValidator();
        var command = new UpdateSessionCommand(Guid.NewGuid(), "teacher", " " + new string('x', 200) + " ");

        Assert.True(validator.Validate(command).IsValid);
        Assert.False(validator.Validate(command with { Title = new string('x', 201) }).IsValid);
        Assert.False(validator.Validate(command with { SessionId = Guid.Empty }).IsValid);
        Assert.False(validator.Validate(command with { TeacherId = " " }).IsValid);
    }

    [Fact]
    public void RenamePreservesSessionMetadataGenerationAndArchives()
    {
        var session = CreateSession();
        session.Clear(1, CreatedAt.AddHours(1));
        var archive = session.Archive(2, CreatedAt.AddHours(2));
        session.ClearDomainEvents();
        var before = SessionResult.From(session);
        var generationStartedAt = session.CurrentGenerationStartedAtUtc;
        var code = session.Code;
        var teacherCode = session.TeacherCode;

        session.Rename("  New title  ");

        Assert.Equal(before with { Title = "New title" }, SessionResult.From(session));
        Assert.Same(code, session.Code);
        Assert.Same(teacherCode, session.TeacherCode);
        Assert.Equal(generationStartedAt, session.CurrentGenerationStartedAtUtc);
        Assert.Same(archive, Assert.Single(session.Archives));
        Assert.Equal("Original title", archive.Title);
        var renamed = Assert.IsType<SessionRenamedDomainEvent>(Assert.Single(session.GetDomainEvents()));
        Assert.Equal(session.Id, renamed.SessionId);
        Assert.Equal(3, renamed.Generation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n ")]
    public void RenameRejectsBlankTitleWithoutMutation(string? title)
    {
        var session = CreateSession();

        Assert.ThrowsAny<ArgumentException>(() => session.Rename(title!));

        Assert.Equal("Original title", session.Title);
        Assert.Empty(session.GetDomainEvents());
    }

    [Fact]
    public void RenameChecksLengthBeforeMutationAndAcceptsTheTrimmedLimit()
    {
        var session = CreateSession();

        Assert.ThrowsAny<ArgumentException>(() => session.Rename(new string('x', 201)));
        Assert.Equal("Original title", session.Title);
        Assert.Empty(session.GetDomainEvents());

        session.Rename(" " + new string('x', 200) + " ");
        Assert.Equal(new string('x', 200), session.Title);
    }

    [Fact]
    public void RenameToTheSameTrimmedTitleDoesNotRaiseAnotherChange()
    {
        var session = CreateSession();

        session.Rename("  Original title  ");

        Assert.Equal("Original title", session.Title);
        Assert.Empty(session.GetDomainEvents());
    }

    private static Session CreateSession() => Session.Create("Original title", new SessionCode("ABCD2345"),
        new SessionCode("EFGH6789"), "owner", "Owner Name", CreatedAt);
}

public sealed partial class CommandBusTests
{
    [Theory]
    [InlineData("owner")]
    [InlineData("colleague")]
    public async Task OwnerAndJoinedTeacherCanRenameWithoutChangingSessionOrStudentAccess(string caller)
    {
        var session = await new CreateSessionCommand("owner", "Original title").ExecuteAsync();
        var studentId = AuthenticateStudent();
        await new JoinSessionCommand(session.Code, studentId).ExecuteAsync();
        if (caller == "colleague")
            await new JoinTeacherSessionCommand(session.TeacherCode, caller).ExecuteAsync();
        var request = HelpRequest.Create(session.Id, 1, studentId, HelpType.Digital,
            new MessageText("My question"), DateTimeOffset.UtcNow);
        _requests.Saved.Add(request);
        Authenticate("Teacher", caller);

        using var response = await _client.PatchAsJsonAsync($"/api/teachers/sessions/{session.Id}", new
        {
            Title = "  New title  ", TeacherId = "spoofed", Id = Guid.NewGuid(),
            Code = "ZZZZZZZZ", TeacherCode = "YYYYYYYY", CurrentGeneration = 99
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(session with { Title = "New title" }, await response.Content.ReadFromJsonAsync<SessionResult>());
        Assert.Equal(session with { Title = "New title" },
            await _client.GetFromJsonAsync<SessionResult>($"/api/teachers/sessions/{session.Id}"));
        var studentSession = (await _client.GetFromJsonAsync<SessionSummary>($"/api/students/sessions/{session.Id}"))!;
        Assert.Equal((session.Id, "New title", 1), (studentSession.Id, studentSession.Title, studentSession.CurrentGeneration));
        Assert.Same(request, Assert.Single(_requests.Saved));
        Assert.Equal(HelpRequestStatus.Pending, request.Status);
        Assert.Equal("My question", Assert.Single(request.Messages).Text.Value);
    }

    [Fact]
    public async Task RenameUsesAuthenticatedTeacherAndHidesUnknownOrUnrelatedSessions()
    {
        var session = await new CreateSessionCommand("owner", "Original title").ExecuteAsync();
        await new JoinSessionCommand(session.Code, "outsider").ExecuteAsync();
        Authenticate("Teacher", "outsider");

        foreach (var id in new[] { session.Id, Guid.NewGuid() })
        {
            using var response = await _client.PatchAsJsonAsync($"/api/teachers/sessions/{id}",
                new { Title = "Unauthorized change", TeacherId = "owner" });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal(session, await new GetSessionQuery(session.Id, "owner").ExecuteAsync());
    }

    [Fact]
    public async Task RenameRejectsMissingInvalidAndOverlongTitlesBeforeMutation()
    {
        var session = await new CreateSessionCommand("owner", "Original title").ExecuteAsync();
        Authenticate("Teacher", "owner");
        foreach (var title in new[] { null, "", " \t ", new string('x', 201) })
        {
            using var response = await _client.PatchAsJsonAsync($"/api/teachers/sessions/{session.Id}", new { Title = title });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var missing = await _client.PatchAsJsonAsync($"/api/teachers/sessions/{session.Id}", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(session, await new GetSessionQuery(session.Id, "owner").ExecuteAsync());
    }

    [Theory]
    [InlineData(null, "Bifrost", HttpStatusCode.Unauthorized)]
    [InlineData("Student", "Bifrost", HttpStatusCode.Forbidden)]
    [InlineData("Teacher", "wrong-scope", HttpStatusCode.Forbidden)]
    public async Task RenameRequiresAuthenticatedTeacherWithScope(string? role, string scope, HttpStatusCode expected)
    {
        if (role is not null) Authenticate(role, "caller", scope);

        using var response = await _client.PatchAsJsonAsync($"/api/teachers/sessions/{Guid.NewGuid()}", new { Title = "New title" });

        Assert.Equal(expected, response.StatusCode);
    }
}
