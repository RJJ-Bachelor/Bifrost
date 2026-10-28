using EirService.HelpRequests.Application.Features.Student.GetStudentQueue;
using EirService.HelpRequests.Application.Features.Student.GetStudentQueueStatus;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Application.Features.Teacher.JoinSession;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using FastEndpoints;
using Shared.Domain.Notifications;

namespace EirService.Test;

public sealed partial class CommandBusTests
{
    [Theory]
    [InlineData("Reserved", true)]
    [InlineData("FaqAccepted", false)]
    [InlineData("AwaitingFaqCancelled", false)]
    [InlineData("AwaitingConfirmationCancelled", false)]
    [InlineData("Created", true)]
    [InlineData("StudentMessageAdded", true)]
    [InlineData("Started", true)]
    [InlineData("Completed", true)]
    [InlineData("Answered", true)]
    [InlineData("Cancelled", true)]
    [InlineData("ReturnedToQueue", true)]
    [InlineData("FaqLinked", true)]
    [InlineData("FaqCreated", true)]
    [InlineData("FaqRejected", true)]
    public async Task SessionNotificationsOnlyReachOtherStudentsWhenTheirQueueDataCanChange(
        string action, bool affectsOtherStudents)
    {
        var session = await new CreateSessionCommand("owner", "Shared queue").ExecuteAsync();
        await new JoinTeacherSessionCommand(session.TeacherCode, "colleague").ExecuteAsync();
        await new JoinTeacherSessionCommand(session.TeacherCode, "owner").ExecuteAsync();
        await new JoinSessionCommand(session.Code, "student").ExecuteAsync();
        await new JoinSessionCommand(session.Code, "observer").ExecuteAsync();
        await new JoinSessionCommand(session.Code, "idle").ExecuteAsync();
        var now = DateTimeOffset.UtcNow;
        var faqId = Guid.NewGuid();
        var request = action == "FaqCreated"
            ? HelpRequest.CreateFaq(faqId, session.Id, 1, new MessageText("Shared question"), now)
            : HelpRequest.Create(session.Id, 1, "student",
                action == "Answered" ? HelpType.Digital : HelpType.Physical, new MessageText("Private question"), now);
        _requests.Saved.Add(request);

        if (action is "Started" or "Completed" or "Answered" or "ReturnedToQueue")
            request.Reserve("owner", now);
        if (action is "Completed" or "ReturnedToQueue")
            request.Start("owner", now);
        if (action is "FaqAccepted" or "FaqRejected" or "AwaitingFaqCancelled" or "AwaitingConfirmationCancelled")
            request.LinkToFaq(faqId, new MessageText("Shared question"), now);
        if (action is "FaqAccepted" or "FaqRejected" or "AwaitingConfirmationCancelled")
            request.SuggestFaq(faqId, "owner", new MessageText("Shared answer"), now);
        if (action is not ("Created" or "FaqCreated")) request.ClearDomainEvents();

        var observerRequest = HelpRequest.Create(session.Id, 1, "observer", HelpType.Physical,
            new MessageText("Another question"), now.AddMinutes(1));
        observerRequest.ClearDomainEvents();
        _requests.Saved.Add(observerRequest);
        var before = await new GetStudentQueueQuery(session.Id, "observer").ExecuteAsync();
        var idleBefore = await new GetStudentQueueStatusQuery(session.Id, "idle").ExecuteAsync();

        switch (action)
        {
            case "Reserved": request.Reserve("owner", now); break;
            case "Started": request.Start("owner", now); break;
            case "Completed": request.Complete("owner", now.AddMinutes(3)); break;
            case "Answered": request.Answer("owner", new MessageText("Private answer"), now); break;
            case "StudentMessageAdded": request.AddStudentMessage("student", new MessageText("Follow-up"), now); break;
            case "Cancelled" or "AwaitingFaqCancelled" or "AwaitingConfirmationCancelled":
                request.Cancel("student", AuthorRole.Student, now); break;
            case "ReturnedToQueue": request.Cancel("owner", AuthorRole.Teacher, now); break;
            case "FaqLinked": request.LinkToFaq(faqId, new MessageText("Shared question"), now); break;
            case "FaqAccepted": request.AcceptFaq("student", now); break;
            case "FaqRejected": request.RejectFaq("student", now); break;
        }
        await _requests.SaveChangesAsync();

        var after = await new GetStudentQueueQuery(session.Id, "observer").ExecuteAsync();
        if (!affectsOtherStudents) Assert.Equal(before, after);
        if (action == "Reserved")
            Assert.Equal(before with { UnreservedWaitingCount = before.UnreservedWaitingCount - 1 }, after);
        if (action == "Completed")
        {
            Assert.NotEqual(before.EstimatedWaitMinutes, after.EstimatedWaitMinutes);
            Assert.Equal(new StudentQueueStatusResult(1, null), idleBefore);
            Assert.Equal(new StudentQueueStatusResult(1, 3),
                await new GetStudentQueueStatusQuery(session.Id, "idle").ExecuteAsync());
            Assert.All(_requests.Notifications,
                item => Assert.Equal(NotificationDeliveryType.RealtimeOnly, item.Message.DeliveryType));
        }
        var expected = affectsOtherStudents
            ? new[] { "colleague", "idle", "observer", "owner", "student" }
            : new[] { "colleague", "owner", "student" };
        Assert.Equal(expected, _requests.Notifications.Select(item => item.Message.UserId).Order());
        Assert.Equal(expected.Length, _requests.Notifications.Select(item => item.Id).Distinct().Count());
        var expectedChange = action is "AwaitingFaqCancelled" or "AwaitingConfirmationCancelled" ? "FaqCancelled" : action;
        foreach (var (_, message) in _requests.Notifications)
        {
            Assert.Equal(session.Id, message.SessionId);
            Assert.Equal(1, message.Generation);
            Assert.DoesNotContain("Private answer", message.Messages);
            Assert.Equal(expectedChange, message.Change);
            if (message.DeliveryType == NotificationDeliveryType.RealtimeOnly)
            {
                Assert.Equal(NotificationEventType.SessionChanged, message.EventType);
                if (message.UserId is "observer" or "idle" || request.IsFaq && message.UserId == "student")
                {
                    Assert.Null(message.HelpRequestId);
                    Assert.Null(message.Status);
                }
                else
                {
                    Assert.Equal(request.Id, message.HelpRequestId);
                    Assert.Equal(request.Status.ToString(), message.Status);
                }
            }
        }
        if (action is "Started" or "Answered" or "FaqLinked")
        {
            var personal = Assert.Single(_requests.Notifications, item => item.Message.UserId == "student").Message;
            Assert.Equal(NotificationDeliveryType.RealtimeOrPush, personal.DeliveryType);
            Assert.Equal(request.Id, personal.HelpRequestId);
        }
    }
}
