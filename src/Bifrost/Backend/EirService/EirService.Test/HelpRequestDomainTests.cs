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
        Assert.Null(request.StartedAt);
        Assert.Throws<HelpRequestRuleException>(() => request.Start("other-teacher", Now));
        Assert.Throws<HelpRequestRuleException>(() => request.Answer("teacher", new MessageText("Answer"), Now));
        Assert.Throws<HelpRequestRuleException>(() => request.Complete("teacher", Now));
        request.Start("teacher", Now.AddMinutes(1));
        Assert.Equal(HelpRequestStatus.Active, request.Status);
        Assert.Throws<HelpRequestRuleException>(() => request.Cancel("student", AuthorRole.Student, Now));
        request.Complete("teacher", Now.AddMinutes(5));
        Assert.Equal(HelpRequestStatus.CompletedByPhysical, request.Status);
        Assert.Equal(Now.AddMinutes(1), request.StartedAt);
        Assert.Equal(Now.AddMinutes(5), request.ClosedAt);
        Assert.Throws<HelpRequestRuleException>(() => request.Complete("teacher", Now));
    }

    [Fact]
    public void DigitalReservationMeasuresTimeUntilAnswer()
    {
        var request = Create(HelpType.Digital);
        var reservedAt = Now.AddMinutes(1).ToOffset(TimeSpan.FromHours(2));
        request.Reserve("teacher", reservedAt);

        request.Answer("teacher", new MessageText("Answer"), reservedAt.AddSeconds(30));

        Assert.Equal(HelpRequestStatus.CompletedByDigital, request.Status);
        Assert.Equal(reservedAt.ToUniversalTime(), request.StartedAt);
        Assert.Equal(TimeSpan.Zero, request.StartedAt!.Value.Offset);
        Assert.Equal(reservedAt.AddSeconds(30).ToUniversalTime(), request.ClosedAt);
        Assert.Equal(TimeSpan.FromSeconds(30), request.ClosedAt!.Value - request.StartedAt.Value);
    }

    [Fact]
    public void ReturnedDigitalHelpMeasuresOnlyTheNewReservation()
    {
        var request = Create(HelpType.Digital);
        request.Reserve("teacher", Now);
        request.Cancel("teacher", AuthorRole.Teacher, Now.AddMinutes(1));
        Assert.Null(request.StartedAt);

        var reservedAt = Now.AddMinutes(3);
        request.Reserve("other-teacher", reservedAt);
        request.Answer("other-teacher", new MessageText("Answer"), reservedAt.AddSeconds(30));

        Assert.Equal(reservedAt, request.StartedAt);
        Assert.Equal(TimeSpan.FromSeconds(30), request.ClosedAt!.Value - request.StartedAt!.Value);
    }

    [Fact]
    public void DigitalAnswerClosesAndStudentFollowUpReopensTheSameConversation()
    {
        var request = Create(HelpType.Digital);
        request.Reserve("teacher", Now);
        Assert.Throws<HelpRequestRuleException>(() => request.Start("teacher", Now));
        request.Answer("teacher", new MessageText("Answer"), Now.AddMinutes(1));
        Assert.Equal(HelpRequestStatus.CompletedByDigital, request.Status);
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

    [Theory]
    [InlineData(HelpType.Digital, HelpRequestStatus.CompletedByDigital)]
    [InlineData(HelpType.Physical, HelpRequestStatus.CompletedByPhysical)]
    [InlineData(HelpType.Digital, HelpRequestStatus.CompletedByFaq)]
    [InlineData(HelpType.Physical, HelpRequestStatus.CompletedByFaq)]
    public void AllCompletionOutcomesAllowAStudentFollowUpToRejoinTheQueue(
        HelpType helpType, HelpRequestStatus completion)
    {
        var request = Create(helpType);
        if (completion == HelpRequestStatus.CompletedByFaq)
        {
            var faqId = Guid.NewGuid();
            request.LinkToFaq(faqId, new MessageText("Shared question"), Now);
            request.SuggestFaq(faqId, "teacher", new MessageText("Shared answer"), Now.AddMinutes(1));
            request.AcceptFaq("student", Now.AddMinutes(2));
        }
        else
        {
            request.Reserve("teacher", Now);
            if (helpType == HelpType.Physical)
            {
                request.Start("teacher", Now.AddMinutes(1));
                request.Complete("teacher", Now.AddMinutes(2));
            }
            else request.Answer("teacher", new MessageText("Answer"), Now.AddMinutes(2));
        }
        var previousMessages = request.Messages.ToArray();
        Assert.Equal(completion, request.Status);
        Assert.True(request.IsCompleted);
        Assert.False(request.IsOpen);
        Assert.Throws<HelpRequestRuleException>(() => request.Reserve("other-teacher", Now));
        Assert.Throws<HelpRequestRuleException>(() => request.Cancel("student", AuthorRole.Student, Now));

        request.AddStudentMessage("student", new MessageText("Follow-up"), Now.AddMinutes(3));

        Assert.Equal(HelpRequestStatus.Pending, request.Status);
        Assert.True(request.IsOpen);
        Assert.False(request.IsCompleted);
        Assert.Equal(Now.AddMinutes(3), request.QueuedAt);
        Assert.Null(request.AssignedTeacherId);
        Assert.Null(request.FaqAnswerId);
        Assert.Null(request.StartedAt);
        Assert.Null(request.ClosedAt);
        Assert.Equal(previousMessages, request.Messages.Take(previousMessages.Length));
        Assert.Equal(HelpRequestMessageType.FollowUpQuestion, request.Messages.Last().MessageType);
        request.Reserve("other-teacher", Now.AddMinutes(4));
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
        var faqId = Guid.NewGuid();
        request.LinkToFaq(faqId, new MessageText("Shared question"), Now);
        request.SuggestFaq(faqId, "teacher", new MessageText("Shared answer"), Now.AddMinutes(1));
        Assert.Throws<HelpRequestRuleException>(() => request.RejectFaq("other", Now));
        request.RejectFaq("student", Now.AddMinutes(2));
        Assert.Equal(Now, request.QueuedAt);
        Assert.Equal(HelpRequestStatus.Pending, request.Status);
        var secondFaqId = Guid.NewGuid();
        request.LinkToFaq(secondFaqId, new MessageText("Another shared question"), Now.AddMinutes(2));
        request.SuggestFaq(secondFaqId, "teacher", new MessageText("Another shared answer"), Now.AddMinutes(3));
        request.AcceptFaq("student", Now.AddMinutes(3));
        Assert.Equal(HelpRequestStatus.CompletedByFaq, request.Status);
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
