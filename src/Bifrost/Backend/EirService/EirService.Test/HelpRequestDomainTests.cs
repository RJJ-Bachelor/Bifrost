using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;

namespace EirService.Test;

public sealed class HelpRequestDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
    private static HelpRequest Create(HelpType type) =>
        HelpRequest.Create(Guid.NewGuid(), 1, "student", type, new MessageText("Question"), Now);

    [Fact]
    public void PhysicalHelpMustBeReservedStartedAndCompleted()
    {
        var request = Create(HelpType.Physical);
        Assert.Throws<HelpRequestRuleException>(() => request.Start("teacher", Now));
        request.Reserve("teacher", Now);
        Assert.Throws<HelpRequestRuleException>(() => request.Start("other-teacher", Now));
        Assert.Throws<HelpRequestRuleException>(() => request.Answer("teacher", new MessageText("Answer"), Now));
        Assert.Throws<HelpRequestRuleException>(() => request.Complete("teacher", Now));
        request.Start("teacher", Now.AddMinutes(1));
        Assert.Equal(HelpRequestStatus.Active, request.Status);
        Assert.Throws<HelpRequestRuleException>(() => request.Cancel("student", AuthorRole.Student, Now));
        request.Complete("teacher", Now.AddMinutes(5));
        Assert.Equal(HelpRequestStatus.Completed, request.Status);
        Assert.Equal(Now.AddMinutes(1), request.StartedAt);
        Assert.Equal(Now.AddMinutes(5), request.ClosedAt);
        Assert.Throws<HelpRequestRuleException>(() => request.Complete("teacher", Now));
    }

    [Fact]
    public void DigitalAnswerClosesAndStudentFollowUpReopensTheSameConversation()
    {
        var request = Create(HelpType.Digital);
        request.Reserve("teacher", Now);
        Assert.Throws<HelpRequestRuleException>(() => request.Start("teacher", Now));
        request.Answer("teacher", new MessageText("Answer"), Now.AddMinutes(1));
        Assert.Equal(HelpRequestStatus.Completed, request.Status);
        request.AddStudentMessage("student", new MessageText("Follow-up"), Now.AddMinutes(2));
        Assert.Equal(HelpRequestStatus.Pending, request.Status);
        Assert.Equal(Now.AddMinutes(2), request.QueuedAt);
        Assert.Null(request.AssignedTeacherId);
        Assert.Null(request.ClosedAt);
        Assert.Null(request.StartedAt);
        Assert.Equal(new[] { 1, 2, 3 }, request.Messages.Select(message => message.SequenceNumber));
        Assert.Equal(new[] { HelpRequestMessageType.StudentQuestion, HelpRequestMessageType.TeacherAnswer,
            HelpRequestMessageType.FollowUpQuestion }, request.Messages.Select(message => message.MessageType));
        Assert.All(request.Messages, message => Assert.Equal(request.Id, message.HelpRequestId));
    }

    [Fact]
    public void OwnershipReservationAndCancellationAreEnforced()
    {
        var request = Create(HelpType.Digital);
        Assert.Throws<HelpRequestRuleException>(() => request.AddStudentMessage("other", new MessageText("Question"), Now));
        request.Reserve("teacher", Now);
        Assert.Throws<HelpRequestRuleException>(() => request.Reserve("teacher-2", Now));
        Assert.Throws<HelpRequestRuleException>(() => request.AddStudentMessage("student", new MessageText("Question"), Now));
        request.Cancel("student", AuthorRole.Student, Now);
        Assert.Throws<HelpRequestRuleException>(() => request.AddStudentMessage("student", new MessageText("Question"), Now));
    }

    [Fact]
    public void TeacherCancellationReturnsActiveHelpAndClearsTheReservation()
    {
        var request = Create(HelpType.Physical);
        request.Reserve("teacher", Now);
        request.Start("teacher", Now.AddMinutes(1));
        Assert.Throws<HelpRequestRuleException>(() => request.Cancel("other-teacher", AuthorRole.Teacher, Now));
        request.Cancel("teacher", AuthorRole.Teacher, Now.AddMinutes(2));
        Assert.Equal(HelpRequestStatus.Pending, request.Status);
        Assert.Equal(Now.AddMinutes(2), request.ReturnedToQueueAt);
        Assert.Equal(Now, request.QueuedAt);
        Assert.Null(request.AssignedTeacherId);
        Assert.Null(request.StartedAt);
        Assert.Null(request.ClosedAt);
        Assert.Single(request.Messages);
        request.Reserve("other-teacher", Now.AddMinutes(3));
        request.Start("other-teacher", Now.AddMinutes(3));
        Assert.Equal(HelpRequestStatus.Active, request.Status);
    }

    [Fact]
    public void StudentFollowUpClearsReturnPriorityAndCancellationClosesTheRequest()
    {
        var request = Create(HelpType.Digital);
        request.Reserve("teacher", Now);
        request.Cancel("teacher", AuthorRole.Teacher, Now.AddMinutes(1));
        request.AddStudentMessage("student", new MessageText("Follow-up"), Now.AddMinutes(2));
        Assert.Null(request.ReturnedToQueueAt);
        Assert.Equal(Now.AddMinutes(2), request.QueuedAt);
        request.Cancel("student", AuthorRole.Student, Now.AddMinutes(3));
        Assert.Equal(HelpRequestStatus.Cancelled, request.Status);
        Assert.Equal(Now.AddMinutes(3), request.ClosedAt);
        Assert.False(request.IsOpen);
    }

    [Fact]
    public void PreparedFaqTransitionsPreserveQueuePositionOnRejection()
    {
        var request = Create(HelpType.Digital);
        Assert.Throws<HelpRequestRuleException>(() => request.AcceptFaq("student", Now));
        typeof(HelpRequest).GetProperty(nameof(HelpRequest.Status))!.SetValue(request, HelpRequestStatus.AwaitingConfirmation);
        typeof(HelpRequest).GetProperty(nameof(HelpRequest.FaqAnswerId))!.SetValue(request, Guid.NewGuid());
        Assert.Throws<HelpRequestRuleException>(() => request.RejectFaq("other", Now));
        request.RejectFaq("student", Now.AddMinutes(2));
        Assert.Equal(Now, request.QueuedAt);
        Assert.Equal(HelpRequestStatus.Pending, request.Status);
        typeof(HelpRequest).GetProperty(nameof(HelpRequest.Status))!.SetValue(request, HelpRequestStatus.AwaitingConfirmation);
        typeof(HelpRequest).GetProperty(nameof(HelpRequest.FaqAnswerId))!.SetValue(request, Guid.NewGuid());
        request.AcceptFaq("student", Now.AddMinutes(3));
        Assert.Equal(HelpRequestStatus.Completed, request.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void MessageTextRejectsEmptyText(string text) => Assert.Throws<ArgumentException>(() => new MessageText(text));

    [Fact]
    public void MessageTextValidatesLengthAndNormalizesText()
    {
        Assert.Throws<ArgumentException>(() => new MessageText(new string('x', 4001)));
        Assert.Equal("Question", new MessageText(" Question ").Value);
        Assert.Equal(4000, new MessageText(new string('x', 4000)).Value.Length);
    }
}
