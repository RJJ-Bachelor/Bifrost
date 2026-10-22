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

    [Fact]
    public void LinkedRequestCannotBeTakenAndRejectionPreservesItsPosition()
    {
        var request = HelpRequest.Create(Guid.NewGuid(), 1, "student", HelpType.Physical, new MessageText("Question"), Now);
        var faqId = Guid.NewGuid();
        request.LinkToFaq(faqId, new MessageText("Shared question"), Now.AddMinutes(1));
        var generalization = request.Messages.Last();
        Assert.Equal("Shared question", generalization.Text.Value);
        Assert.Equal(AuthorRole.System, generalization.AuthorRole);
        Assert.Equal("mimir", generalization.AuthorId);
        Assert.Equal(2, generalization.SequenceNumber);
        Assert.Equal(Now.AddMinutes(1), generalization.CreatedAt);
        Assert.Throws<HelpRequestRuleException>(() => request.LinkToFaq(faqId, new MessageText("Shared question"), Now));
        Assert.Equal(2, request.Messages.Count);
        Assert.True(request.IsOpen);
        Assert.Throws<HelpRequestRuleException>(() => request.Reserve("teacher", Now));
        Assert.Throws<HelpRequestRuleException>(() => request.AcceptFaq("student", Now));
        request.SuggestFaq(faqId, "teacher", new MessageText("Answer"), Now.AddMinutes(2));
        Assert.Throws<HelpRequestRuleException>(() => request.AcceptFaq("other-student", Now));
        request.RejectFaq("student", Now.AddMinutes(3));
        Assert.Equal(HelpRequestStatus.Pending, request.Status);
        Assert.Equal(Now, request.QueuedAt);
        Assert.Null(request.FaqAnswerId);
        request.Reserve("teacher", Now.AddMinutes(4));
    }

    [Theory]
    [InlineData("Reserved")]
    [InlineData("Active")]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    public void GeneralizationCannotChangeUnavailableRequests(string state)
    {
        var request = HelpRequest.Create(Guid.NewGuid(), 1, "student", HelpType.Physical, new MessageText("Question"), Now);
        if (state == "Cancelled") request.Cancel("student", AuthorRole.Student, Now);
        else
        {
            request.Reserve("teacher", Now);
            if (state != "Reserved") request.Start("teacher", Now);
            if (state == "Completed") request.Complete("teacher", Now);
        }
        var status = request.Status;

        Assert.Throws<HelpRequestRuleException>(() => request.LinkToFaq(Guid.NewGuid(), new MessageText("Shared question"), Now));

        Assert.Equal(status, request.Status);
        Assert.Null(request.FaqAnswerId);
        Assert.Single(request.Messages);
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
        Assert.Equal(HelpRequestStatus.Completed, request.Status);
    }
}
