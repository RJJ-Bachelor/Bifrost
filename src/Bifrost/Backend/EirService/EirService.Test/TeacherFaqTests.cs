using EirService.KnowledgeBase.Application.Features.Teacher.CreateFaq;
using EirService.KnowledgeBase.Domain.DomainEvents;
using EirService.KnowledgeBase.Domain.Entities;

namespace EirService.Test;

public sealed class TeacherFaqTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-29T10:00:00Z");

    [Fact]
    public void TeacherCanCreateAnAnsweredFaqWithoutGeneralizedSources()
    {
        var sessionId = Guid.NewGuid();
        var faq = FaqAnswer.CreateAnswered(sessionId, 3, " Question ", " Answer ", "teacher", Now);

        Assert.NotEqual(Guid.Empty, faq.Id);
        Assert.Equal(sessionId, faq.SessionId);
        Assert.Equal(3, faq.Generation);
        Assert.Equal("Question", faq.Question);
        Assert.Equal("Answer", faq.Answer);
        Assert.Equal("teacher", faq.AnsweredByTeacherId);
        Assert.Equal(Now, faq.CreatedAt);
        Assert.Equal(Now, faq.AnsweredAt);
        Assert.Empty(faq.HelpRequestIds);
        Assert.IsType<FaqAnswerRecordedDomainEvent>(Assert.Single(faq.GetDomainEvents()));
    }

    [Fact]
    public void CorrectedQuestionAndAnswerAreRecordedTogetherAndRedeliveryIsIdempotent()
    {
        var faq = FaqAnswer.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Original question",
            [Guid.NewGuid(), Guid.NewGuid()], Now);

        Assert.Throws<ArgumentException>(() => faq.RecordAnswer(" ", "teacher", Now, "Corrected question"));
        Assert.Equal("Original question", faq.Question);
        Assert.Null(faq.Answer);
        Assert.Empty(faq.GetDomainEvents());

        faq.RecordAnswer(" Answer ", "teacher", Now, " Corrected question ");
        faq.RecordAnswer("Answer", "teacher", Now.AddMinutes(1), "Corrected question");
        Assert.Equal("Corrected question", faq.Question);
        Assert.Equal(Now, faq.AnsweredAt);
        Assert.Single(faq.GetDomainEvents());

        Assert.Throws<InvalidOperationException>(() => faq.RecordAnswer("Answer", "teacher", Now, "Another question"));
        Assert.Equal("Corrected question", faq.Question);
        Assert.Equal("Answer", faq.Answer);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TeacherFaqRequiresAValidSessionAndGeneration(int generation)
    {
        Assert.Throws<ArgumentException>(() => FaqAnswer.CreateAnswered(Guid.NewGuid(), generation,
            "Question", "Answer", "teacher", Now));
        Assert.Throws<ArgumentException>(() => FaqAnswer.CreateAnswered(Guid.Empty, 1,
            "Question", "Answer", "teacher", Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TeacherFaqRequiresQuestionAndAnswer(string? text)
    {
        var validator = new CreateFaqCommandValidator();
        Assert.False(validator.Validate(new CreateFaqCommand(Guid.NewGuid(), "teacher", text!, "Answer")).IsValid);
        Assert.False(validator.Validate(new CreateFaqCommand(Guid.NewGuid(), "teacher", "Question", text!)).IsValid);
        Assert.Throws<ArgumentException>(() => FaqAnswer.CreateAnswered(Guid.NewGuid(), 1, text!, "Answer", "teacher", Now));
        Assert.Throws<ArgumentException>(() => FaqAnswer.CreateAnswered(Guid.NewGuid(), 1, "Question", text!, "teacher", Now));
    }

    [Fact]
    public void TeacherFaqTextLimitsApplyAfterTrimmingAndRequireTrustedIdentifiers()
    {
        var validator = new CreateFaqCommandValidator();
        var maximum = " " + new string('a', FaqAnswer.MaxTextLength) + " ";
        Assert.True(validator.Validate(new CreateFaqCommand(Guid.NewGuid(), "teacher", maximum, maximum)).IsValid);
        var tooLong = new string('a', FaqAnswer.MaxTextLength + 1);
        Assert.False(validator.Validate(new CreateFaqCommand(Guid.NewGuid(), "teacher", tooLong, "Answer")).IsValid);
        Assert.False(validator.Validate(new CreateFaqCommand(Guid.NewGuid(), "teacher", "Question", tooLong)).IsValid);
        Assert.False(validator.Validate(new CreateFaqCommand(Guid.Empty, "teacher", "Question", "Answer")).IsValid);
        Assert.False(validator.Validate(new CreateFaqCommand(Guid.NewGuid(), " ", "Question", "Answer")).IsValid);
    }
}
