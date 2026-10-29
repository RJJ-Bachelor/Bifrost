using EirService.HelpRequests.Application.Features.Teacher.AnswerHelpRequest;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;

namespace EirService.Test;

public sealed class FaqQuestionEditingTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-29T10:00:00Z");

    [Fact]
    public void AnswerCanReviseTheFaqQuestionWithoutReplacingTheConversationMessage()
    {
        var request = HelpRequest.CreateFaq(Guid.NewGuid(), Guid.NewGuid(), 1, new MessageText("Original question"), Now);
        var question = Assert.Single(request.Messages);
        var questionId = question.Id;
        request.Reserve("teacher", Now.AddMinutes(1));

        request.Answer("teacher", new MessageText("Answer"), Now.AddMinutes(2), new MessageText(" Revised question "));

        Assert.Equal("Revised question", question.Text.Value);
        Assert.Equal(questionId, question.Id);
        Assert.Equal("mimir", question.AuthorId);
        Assert.Equal(AuthorRole.System, question.AuthorRole);
        Assert.Equal(1, question.SequenceNumber);
        Assert.Equal(Now, question.CreatedAt);
        Assert.Equal(HelpRequestStatus.CompletedByDigital, request.Status);
        Assert.Equal(2, request.Messages.Count);
        Assert.Equal("Answer", request.Messages.Last().Text.Value);
    }

    [Fact]
    public void FailedOrRepeatedAnswersCannotChangeTheFaqQuestion()
    {
        var request = HelpRequest.CreateFaq(Guid.NewGuid(), Guid.NewGuid(), 1, new MessageText("Original question"), Now);
        var answer = new MessageText("Answer");
        var question = new MessageText("Rejected edit");
        Assert.Throws<HelpRequestRuleException>(() => request.Answer("teacher", answer, Now, question));
        request.Reserve("teacher", Now);
        Assert.Throws<HelpRequestRuleException>(() => request.Answer("another-teacher", answer, Now, question));
        Assert.Equal("Original question", Assert.Single(request.Messages).Text.Value);

        request.Answer("teacher", answer, Now);

        Assert.Throws<HelpRequestRuleException>(() => request.Answer("teacher", answer, Now, question));
        Assert.Equal("Original question", request.Messages.First().Text.Value);
        Assert.Equal(2, request.Messages.Count);
    }

    [Fact]
    public void AnswerCannotEditAStudentsOwnQuestion()
    {
        var request = HelpRequest.Create(Guid.NewGuid(), 1, "student", HelpType.Digital, new MessageText("Private question"), Now);
        request.Reserve("teacher", Now);

        Assert.Throws<HelpRequestRuleException>(() => request.Answer("teacher", new MessageText("Answer"), Now,
            new MessageText("Edited question")));

        Assert.Equal(HelpRequestStatus.Pending, request.Status);
        Assert.Equal("Private question", Assert.Single(request.Messages).Text.Value);
        request.Answer("teacher", new MessageText("Answer"), Now);
        Assert.Equal(HelpRequestStatus.CompletedByDigital, request.Status);
    }

    [Fact]
    public void RevisedFaqSuggestionPreservesOriginalAndPreviouslyRejectedQuestions()
    {
        var request = HelpRequest.Create(Guid.NewGuid(), 1, "student", HelpType.Physical, new MessageText("Private question"), Now);
        var rejectedFaqId = Guid.NewGuid();
        request.LinkToFaq(rejectedFaqId, new MessageText("Previous FAQ"), Now);
        request.SuggestFaq(rejectedFaqId, "teacher", new MessageText("Previous answer"), Now);
        request.RejectFaq("student", Now);
        var rejected = request.Messages.Single(message => message.MessageType == HelpRequestMessageType.FaqQuestion);
        var currentFaqId = Guid.NewGuid();
        request.LinkToFaq(currentFaqId, new MessageText("Current FAQ"), Now.AddMinutes(1));

        request.SuggestFaq(currentFaqId, "teacher", new MessageText("Current answer"), Now.AddMinutes(2),
            new MessageText("Revised current FAQ"));

        Assert.Equal("Private question", request.Messages.First().Text.Value);
        Assert.Equal("Previous FAQ", rejected.Text.Value);
        Assert.True(rejected.IsRejected);
        Assert.Equal("Revised current FAQ", request.Messages.Single(message => !message.IsRejected &&
            message.MessageType == HelpRequestMessageType.FaqQuestion).Text.Value);
        Assert.Equal(HelpRequestStatus.AwaitingConfirmation, request.Status);
        Assert.Equal(Enumerable.Range(1, 5), request.Messages.Select(message => message.SequenceNumber));
    }

    [Fact]
    public void QuestionValidationKeepsTheExistingAnswerContractCompatible()
    {
        var validator = new AnswerHelpRequestCommandValidator();
        var command = new AnswerHelpRequestCommand(Guid.NewGuid(), "teacher", "Answer");
        Assert.True(validator.Validate(command).IsValid);
        Assert.True(validator.Validate(command with { Question = new string('q', MessageText.MaxLength) }).IsValid);

        foreach (var question in new[] { "", " ", new string('q', MessageText.MaxLength + 1) })
        {
            var failure = Assert.Single(validator.Validate(command with { Question = question }).Errors);
            Assert.Equal(nameof(AnswerHelpRequestCommand.Question), failure.PropertyName);
        }
    }
}
