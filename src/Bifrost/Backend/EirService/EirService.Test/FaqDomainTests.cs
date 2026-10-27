using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.KnowledgeBase.Domain.Entities;

namespace EirService.Test;

public sealed class FaqDomainTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-20T10:00:00Z");

    [Fact]
    public void FaqRequiresMultipleSourcesAndCanOnlyBeAnsweredOnce()
    {
        var source = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => FaqAnswer.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Question", [source, source], Now));
        var faq = FaqAnswer.Create(Guid.NewGuid(), Guid.NewGuid(), 1, " Question ", [source, Guid.NewGuid()], Now);
        faq.RecordAnswer(" Answer ", "teacher", Now);
        faq.RecordAnswer("Answer", "teacher", Now.AddMinutes(1));
        Assert.Equal("Answer", faq.Answer);
        Assert.Equal(Now, faq.AnsweredAt);
        Assert.Throws<InvalidOperationException>(() => faq.RecordAnswer("Different answer", "other", Now));
    }

    [Fact]
    public void GeneralizationCannotIncludeItselfOrMoreThanOneHundredSources()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => FaqAnswer.Create(id, Guid.NewGuid(), 1, "Question", [id, Guid.NewGuid()], Now));
        var sources = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Throws<ArgumentException>(() => FaqAnswer.Create(id, Guid.NewGuid(), 1, "Question", sources, Now));
        Assert.Equal(100, FaqAnswer.Create(id, Guid.NewGuid(), 1, "Question", sources.Take(100), Now).HelpRequestIds.Count);
    }

    [Theory]
    [InlineData(HelpType.Digital)]
    [InlineData(HelpType.Physical)]
    public void LinkedRequestCannotBeTakenAndRejectionPreservesItsPosition(HelpType helpType)
    {
        var request = HelpRequest.Create(Guid.NewGuid(), 1, "student", helpType, new MessageText("Question"), Now);
        var faqId = Guid.NewGuid();
        request.LinkToFaq(faqId, new MessageText("Shared question"), Now.AddMinutes(1));
        var generalization = request.Messages.Last();
        Assert.Equal("Shared question", generalization.Text.Value);
        Assert.Equal(AuthorRole.System, generalization.AuthorRole);
        Assert.Equal(HelpRequestMessageType.FaqQuestion, generalization.MessageType);
        Assert.Equal("mimir", generalization.AuthorId);
        Assert.Equal(2, generalization.SequenceNumber);
        Assert.Equal(Now.AddMinutes(1), generalization.CreatedAt);
        Assert.Throws<HelpRequestRuleException>(() => request.LinkToFaq(faqId, new MessageText("Shared question"), Now));
        Assert.Equal(2, request.Messages.Count);
        Assert.True(request.IsOpen);
        Assert.Throws<HelpRequestRuleException>(() => request.Reserve("teacher", Now));
        Assert.Throws<HelpRequestRuleException>(() => request.AcceptFaq("student", Now));
        request.SuggestFaq(faqId, "teacher", new MessageText("Answer"), Now.AddMinutes(2));
        Assert.Equal(HelpRequestMessageType.FaqAnswer, request.Messages.Last().MessageType);
        Assert.Throws<HelpRequestRuleException>(() => request.AcceptFaq("other-student", Now));
        request.RejectFaq("student", Now.AddMinutes(3));
        Assert.Equal(HelpRequestStatus.Pending, request.Status);
        Assert.Equal(helpType, request.HelpType);
        Assert.Equal(Now, request.QueuedAt);
        Assert.Null(request.FaqAnswerId);
        Assert.False(request.Messages.Single(message => message.MessageType == HelpRequestMessageType.StudentQuestion).IsRejected);
        Assert.All(request.Messages.Where(message => message.MessageType is HelpRequestMessageType.FaqQuestion or HelpRequestMessageType.FaqAnswer),
            message => Assert.True(message.IsRejected));
        request.Reserve("teacher", Now.AddMinutes(4));
    }

    [Fact]
    public void EachRejectedFaqKeepsTheOriginalMessagesAndStableSequenceNumbers()
    {
        var request = HelpRequest.Create(Guid.NewGuid(), 1, "student", HelpType.Digital, new MessageText("Question"), Now);
        request.AddStudentMessage("student", new MessageText("Additional details"), Now);
        var originals = request.Messages.ToArray();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var id = Guid.NewGuid();
            request.LinkToFaq(id, new MessageText("Shared question"), Now.AddMinutes(attempt + 1));
            request.SuggestFaq(id, "teacher", new MessageText("Shared answer"), Now.AddMinutes(attempt + 1));
            Assert.Equal(2, request.Messages.Count(message => !message.IsRejected &&
                message.MessageType is HelpRequestMessageType.FaqQuestion or HelpRequestMessageType.FaqAnswer));

            request.RejectFaq("student", Now.AddMinutes(attempt + 1));

            Assert.Equal(originals, request.Messages.Where(message => !message.IsRejected).ToArray());
            Assert.Equal(Now, request.QueuedAt);
        }
        Assert.Equal(Enumerable.Range(1, 6), request.Messages.Select(message => message.SequenceNumber));
        request.Reserve("teacher", Now.AddMinutes(3));
        request.Answer("teacher", new MessageText("Individual answer"), Now.AddMinutes(4));
        Assert.Equal(7, request.Messages.Last().SequenceNumber);
        Assert.False(request.Messages.Last().IsRejected);
    }

    [Fact]
    public void PhysicalRequestCanAcceptAFaqAndFinishWithoutStartingPhysicalHelp()
    {
        var request = HelpRequest.Create(Guid.NewGuid(), 1, "student", HelpType.Physical, new MessageText("Question"), Now);
        var faqId = Guid.NewGuid();

        Assert.True(request.CanLinkToFaq);
        request.LinkToFaq(faqId, new MessageText("Shared question"), Now.AddMinutes(1));
        request.SuggestFaq(faqId, "teacher", new MessageText("Answer"), Now.AddMinutes(2));
        Assert.Throws<HelpRequestRuleException>(() => request.AcceptFaq("another-student", Now));
        request.AcceptFaq("student", Now.AddMinutes(3));

        Assert.Equal(HelpRequestStatus.CompletedByFaq, request.Status);
        Assert.Equal(HelpType.Physical, request.HelpType);
        Assert.Equal(faqId, request.FaqAnswerId);
        Assert.Equal(Now, request.QueuedAt);
        Assert.Null(request.StartedAt);
        Assert.Null(request.AssignedTeacherId);
        Assert.Equal(Now.AddMinutes(3), request.ClosedAt);
        Assert.Equal(new[] { HelpRequestMessageType.StudentQuestion, HelpRequestMessageType.FaqQuestion, HelpRequestMessageType.FaqAnswer },
            request.Messages.Select(message => message.MessageType));
        Assert.Throws<HelpRequestRuleException>(() => request.AcceptFaq("student", Now));
        Assert.Throws<HelpRequestRuleException>(() => request.RejectFaq("student", Now));
    }

    [Fact]
    public void PhysicalFaqRejectionPreservesReturnPriorityAndAllowsPhysicalHelpToContinue()
    {
        var request = HelpRequest.Create(Guid.NewGuid(), 1, "student", HelpType.Physical, new MessageText("Question"), Now);
        request.Reserve("teacher", Now);
        request.Start("teacher", Now.AddMinutes(1));
        request.Cancel("teacher", AuthorRole.Teacher, Now.AddMinutes(2));
        var returnedAt = request.ReturnedToQueueAt;
        var faqId = Guid.NewGuid();
        request.LinkToFaq(faqId, new MessageText("Shared question"), Now.AddMinutes(3));
        request.SuggestFaq(faqId, "teacher", new MessageText("Answer"), Now.AddMinutes(4));
        request.RejectFaq("student", Now.AddMinutes(5));

        Assert.Equal(HelpRequestStatus.Pending, request.Status);
        Assert.Equal(HelpType.Physical, request.HelpType);
        Assert.Equal(Now, request.QueuedAt);
        Assert.Equal(returnedAt, request.ReturnedToQueueAt);
        Assert.Null(request.FaqAnswerId);
        request.Reserve("teacher", Now.AddMinutes(6));
        Assert.Throws<HelpRequestRuleException>(() => request.Answer("teacher", new MessageText("Individual answer"), Now));
        request.Start("teacher", Now.AddMinutes(7));
        request.Complete("teacher", Now.AddMinutes(8));
        Assert.Equal(HelpRequestStatus.CompletedByPhysical, request.Status);
    }

    [Fact]
    public void AnsweredDigitalQuestionCannotJoinAnotherFaqWhenItsFollowUpIsReopened()
    {
        var request = HelpRequest.Create(Guid.NewGuid(), 1, "student", HelpType.Digital, new MessageText("Question"), Now);
        request.Reserve("teacher", Now);
        request.Answer("teacher", new MessageText("Individual answer"), Now.AddMinutes(1));
        Assert.False(request.CanLinkToFaq);
        request.AddStudentMessage("student", new MessageText("Follow-up"), Now.AddMinutes(2));

        Assert.False(request.CanLinkToFaq);
        Assert.Throws<HelpRequestRuleException>(() => request.LinkToFaq(Guid.NewGuid(), new MessageText("Shared question"), Now));
        Assert.Equal(HelpRequestStatus.Pending, request.Status);
        Assert.Null(request.FaqAnswerId);
        Assert.Equal(3, request.Messages.Count);
        request.Reserve("teacher", Now.AddMinutes(3));
        request.Answer("teacher", new MessageText("Follow-up answer"), Now.AddMinutes(4));
    }

    [Theory]
    [InlineData(HelpType.Digital, true)]
    [InlineData(HelpType.Digital, false)]
    [InlineData(HelpType.Physical, true)]
    [InlineData(HelpType.Physical, false)]
    public void FollowUpAfterFaqAnswerCannotBeCapturedByADelayedGeneralization(HelpType helpType, bool accepted)
    {
        var request = HelpRequest.Create(Guid.NewGuid(), 1, "student", helpType, new MessageText("Question"), Now);
        var faqId = Guid.NewGuid();
        request.LinkToFaq(faqId, new MessageText("Shared question"), Now);
        request.SuggestFaq(faqId, "teacher", new MessageText("Shared answer"), Now.AddMinutes(1));
        if (accepted) request.AcceptFaq("student", Now.AddMinutes(2));
        else
        {
            request.RejectFaq("student", Now.AddMinutes(2));
            Assert.True(request.CanLinkToFaq);
        }
        request.AddStudentMessage("student", new MessageText("Follow-up"), Now.AddMinutes(3));

        Assert.False(request.CanLinkToFaq);
        Assert.Throws<HelpRequestRuleException>(() => request.LinkToFaq(Guid.NewGuid(), new MessageText("Another question"), Now));
        Assert.Equal(HelpRequestStatus.Pending, request.Status);
        Assert.Null(request.FaqAnswerId);
        Assert.Equal(Now.AddMinutes(3), request.QueuedAt);
    }

    [Fact]
    public void AdditionalDetailsBeforeAnAnswerDoNotExcludeTheOriginalQuestionFromGeneralization()
    {
        var request = HelpRequest.Create(Guid.NewGuid(), 1, "student", HelpType.Digital, new MessageText("Question"), Now);
        request.AddStudentMessage("student", new MessageText("Additional details"), Now.AddMinutes(1));

        Assert.True(request.CanLinkToFaq);
        request.LinkToFaq(Guid.NewGuid(), new MessageText("Shared question"), Now.AddMinutes(2));
        Assert.Equal(HelpRequestStatus.AwaitingFaq, request.Status);
    }

    [Theory]
    [InlineData(HelpType.Physical, "Reserved")]
    [InlineData(HelpType.Physical, "Active")]
    [InlineData(HelpType.Physical, "Completed")]
    [InlineData(HelpType.Physical, "Cancelled")]
    [InlineData(HelpType.Digital, "Reserved")]
    [InlineData(HelpType.Digital, "Completed")]
    [InlineData(HelpType.Digital, "Cancelled")]
    public void GeneralizationCannotChangeUnavailableRequests(HelpType helpType, string state)
    {
        var request = HelpRequest.Create(Guid.NewGuid(), 1, "student", helpType, new MessageText("Question"), Now);
        if (state == "Cancelled") request.Cancel("student", AuthorRole.Student, Now);
        else
        {
            request.Reserve("teacher", Now);
            if (helpType == HelpType.Physical)
            {
                if (state != "Reserved") request.Start("teacher", Now);
                if (state == "Completed") request.Complete("teacher", Now);
            }
            else if (state == "Completed") request.Answer("teacher", new MessageText("Answer"), Now);
        }
        var status = request.Status;

        Assert.Throws<HelpRequestRuleException>(() => request.LinkToFaq(Guid.NewGuid(), new MessageText("Shared question"), Now));

        Assert.Equal(status, request.Status);
        Assert.Null(request.FaqAnswerId);
        Assert.Equal(helpType == HelpType.Digital && state == "Completed" ? 2 : 1, request.Messages.Count);
    }

    [Fact]
    public void FaqQueueItemHasNoStudentAndKeepsItsFaqWhenReturned()
    {
        var id = Guid.NewGuid();
        var request = HelpRequest.CreateFaq(id, Guid.NewGuid(), 1, new MessageText("Shared question"), Now);
        Assert.True(request.IsFaq);
        Assert.Null(request.StudentId);
        request.Reserve("teacher", Now);
        Assert.Throws<HelpRequestRuleException>(() => request.Answer("other-teacher", new MessageText("Answer"), Now));
        request.Cancel("teacher", AuthorRole.Teacher, Now);
        Assert.Equal(id, request.FaqAnswerId);
        request.Reserve("other-teacher", Now);
        request.Answer("other-teacher", new MessageText("Answer"), Now);
        Assert.Equal(HelpRequestStatus.CompletedByDigital, request.Status);
        Assert.Equal(new[] { HelpRequestMessageType.FaqQuestion, HelpRequestMessageType.FaqAnswer },
            request.Messages.Select(message => message.MessageType));
    }
}
