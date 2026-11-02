using EirService.Contracts.Exceptions;
using EirService.HelpRequests.Application.Features.Student.AddStudentMessage;
using EirService.HelpRequests.Application.Features.Student.CancelHelpRequest;
using EirService.HelpRequests.Application.Features.Student.CreateHelpRequest;
using EirService.HelpRequests.Application.Features.Teacher.ChangeHelpRequestStatus;
using EirService.HelpRequests.Domain.Entities;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Application.Features.Teacher.JoinSession;
using EirService.Sessions.Application.Features.Teacher.ArchiveSession;
using EirService.Sessions.Application.Features.Teacher.ClearSession;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using FastEndpoints;

namespace EirService.Test;

public sealed partial class CommandBusTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionLifecycleRequiresTeacherMembership(bool archive)
    {
        var session = await new CreateSessionCommand("owner", "Course").ExecuteAsync();
        await new JoinSessionCommand(session.Code, "outsider").ExecuteAsync();

        if (archive)
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => new ArchiveSessionCommand(session.Id, "outsider", 1).ExecuteAsync());
        else
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => new ClearSessionCommand(session.Id, "outsider", 1).ExecuteAsync());

        await new JoinTeacherSessionCommand(session.TeacherCode, "colleague").ExecuteAsync();
        if (archive)
        {
            var result = await new ArchiveSessionCommand(session.Id, "colleague", 1).ExecuteAsync();
            Assert.Equal((1, 2, "Course"), (result.Generation, result.CurrentGeneration, result.Title));
        }
        else
        {
            var result = await new ClearSessionCommand(session.Id, "colleague", 1).ExecuteAsync();
            Assert.Equal(2, result.CurrentGeneration);
        }
    }

    [Fact]
    public async Task StudentFollowUpAppendsToItsOwnRequestAndCancellationClosesIt()
    {
        var sessionId = await SeedSessionAsync("student-123");
        var request = await new CreateHelpRequestCommand(sessionId, "student-123", "Question", HelpType.Digital).ExecuteAsync();

        var followedUp = await new AddStudentMessageCommand(request.Id, "student-123", "  More detail  ").ExecuteAsync();
        var cancelled = await new CancelHelpRequestCommand(request.Id, "student-123", "Cancel").ExecuteAsync();

        Assert.Equal("More detail", followedUp.Messages.Last().Text);
        Assert.Equal(2, followedUp.Messages.Count);
        Assert.Equal(HelpRequestStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.ClosedAt);
        Assert.Equal(request.Id, cancelled.Id);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("other-student")]
    [InlineData("stale-generation")]
    public async Task StudentMutationsRejectMissingForeignAndArchivedRequestsWithoutChangingTheConversation(string boundary)
    {
        var sessionId = await SeedSessionAsync("student-123");
        var request = await new CreateHelpRequestCommand(sessionId, "student-123", "Question", HelpType.Digital).ExecuteAsync();
        var id = boundary == "missing" ? Guid.NewGuid() : request.Id;
        var caller = boundary == "other-student" ? "other-student" : "student-123";
        if (boundary == "stale-generation")
            await new ClearSessionCommand(sessionId, "teacher-123", 1).ExecuteAsync();
        var stored = Assert.Single(_requests.Saved);
        var outgoingBefore = _requests.Notifications.Count;

        if (boundary == "stale-generation")
        {
            await Assert.ThrowsAsync<OperationConflictException>(() => new AddStudentMessageCommand(id, caller, "Follow-up").ExecuteAsync());
            await Assert.ThrowsAsync<OperationConflictException>(() => new CancelHelpRequestCommand(id, caller, "Cancel").ExecuteAsync());
        }
        else
        {
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => new AddStudentMessageCommand(id, caller, "Follow-up").ExecuteAsync());
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => new CancelHelpRequestCommand(id, caller, "Cancel").ExecuteAsync());
        }

        Assert.Equal(HelpRequestStatus.Pending, stored.Status);
        Assert.Single(stored.Messages);
        Assert.Equal(outgoingBefore, _requests.Notifications.Count);
    }

    [Theory]
    [InlineData(HelpRequestAction.Start)]
    [InlineData(HelpRequestAction.Complete)]
    [InlineData(HelpRequestAction.Cancel)]
    public async Task TeacherStatusCommandsPersistTheRequestedPhysicalHelpTransition(HelpRequestAction action)
    {
        var sessionId = await SeedSessionAsync("student-123");
        var created = await new CreateHelpRequestCommand(sessionId, "student-123", "Physical help", HelpType.Physical).ExecuteAsync();
        var request = Assert.Single(_requests.Saved);
        request.Reserve("teacher-123", DateTimeOffset.UtcNow);
        if (action == HelpRequestAction.Complete) request.Start("teacher-123", DateTimeOffset.UtcNow);

        var result = await new ChangeHelpRequestStatusCommand(created.Id, "teacher-123", action).ExecuteAsync();

        var expected = action switch
        {
            HelpRequestAction.Start => HelpRequestStatus.Active,
            HelpRequestAction.Complete => HelpRequestStatus.CompletedByPhysical,
            _ => HelpRequestStatus.Pending
        };
        Assert.Equal(expected, result.Status);
        Assert.Equal(expected, request.Status);
        Assert.Empty(request.GetDomainEvents());
        Assert.Contains(_requests.Notifications, notification => notification.Message.HelpRequestId == request.Id &&
            notification.Message.Status == expected.ToString());
        if (action == HelpRequestAction.Cancel) Assert.Null(result.AssignedTeacherId);
        if (action == HelpRequestAction.Complete) Assert.NotNull(result.ClosedAt);
    }
}
