using System.Text.Json;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Application.Features.Teacher.JoinSession;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using FastEndpoints;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace EirService.Test;

public sealed partial class CommandBusTests
{
    [Fact]
    public async Task AQueueWith1285StudentsUsesOnePublicRefreshAndKeepsPersonalAnswersSeparate()
    {
        var session = await new CreateSessionCommand("owner", "Large audience").ExecuteAsync();
        string[] teachers = ["owner", "second-teacher", "third-teacher"];
        foreach (var teacherId in teachers.Skip(1))
            await new JoinTeacherSessionCommand(session.TeacherCode, teacherId).ExecuteAsync();
        var students = Enumerable.Range(0, 1285).Select(index => $"student-{index}").ToArray();
        foreach (var studentId in students)
            await new JoinSessionCommand(session.Code, studentId).ExecuteAsync();
        var request = HelpRequest.Create(session.Id, 1, students[0], HelpType.Digital,
            new MessageText("Private question"), DateTimeOffset.UtcNow);
        _requests.Saved.Add(request);

        await _requests.SaveChangesAsync();

        var refresh = Assert.IsType<SessionChangedMessage>(Assert.Single(_requests.Outgoing,
            message => message.Payload is SessionChangedMessage).Payload);
        Assert.Equal(students.Order(), refresh.UserIds.Order());
        var started = _requests.Outgoing.Select(message => message.Payload).OfType<NotificationCreatedMessage>().ToArray();
        Assert.Equal(teachers.Order(), started.Select(message => message.UserId).Order());
        Assert.All(started, message => Assert.Equal(NotificationEventType.HelpRequestCreated, message.EventType));
        Assert.DoesNotContain("Private question", JsonSerializer.Serialize(refresh));
        Assert.DoesNotContain(request.Id.ToString(), JsonSerializer.Serialize(refresh));

        request.Reserve(teachers[0], DateTimeOffset.UtcNow);
        await _requests.SaveChangesAsync();
        _requests.Outgoing.Clear();
        request.Answer(teachers[0], new MessageText("Private answer"), DateTimeOffset.UtcNow);

        await _requests.SaveChangesAsync();

        var answer = Assert.IsType<NotificationCreatedMessage>(Assert.Single(_requests.Outgoing,
            message => message.Payload is NotificationCreatedMessage).Payload);
        Assert.Equal((students[0], request.Id, NotificationEventType.HelpRequestAnswered),
            (answer.UserId, answer.HelpRequestId, answer.EventType));
        refresh = Assert.IsType<SessionChangedMessage>(Assert.Single(_requests.Outgoing,
            message => message.Payload is SessionChangedMessage).Payload);
        Assert.Equal(students.Skip(1).Concat(teachers).Order(), refresh.UserIds.Order());
        Assert.DoesNotContain("Private answer", JsonSerializer.Serialize(refresh));
        Assert.DoesNotContain(request.Id.ToString(), JsonSerializer.Serialize(refresh));
    }
}
