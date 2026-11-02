using EirService.KnowledgeBase.Domain.Entities;
using EirService.Participants.Domain.Entities;
using EirService.Participants.Domain.ValueObjects;
using EirService.Sessions.Domain.ValueObjects;

namespace EirService.Test;

public sealed class DomainDataBoundaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(2));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void ParticipantRequiresAnIdentity(string? subject)
    {
        Assert.ThrowsAny<ArgumentException>(() => SessionParticipant.Join(Guid.NewGuid(), subject!, ParticipantRole.Student, Now));
    }

    [Fact]
    public void ParticipantIdentityIncludesTheRoleAndNormalizesTheJoinTime()
    {
        var sessionId = Guid.NewGuid();
        var student = SessionParticipant.Join(sessionId, "issuer|student", ParticipantRole.Student, Now, "  Student Name  ");
        var teacher = SessionParticipant.Join(sessionId, "issuer|student", ParticipantRole.Teacher, Now);

        Assert.Equal(new SessionParticipantId(sessionId, "issuer|student", ParticipantRole.Student), student.Id);
        Assert.NotEqual(student.Id, teacher.Id);
        Assert.Equal("Student Name", student.DisplayName);
        Assert.Equal(TimeSpan.Zero, student.JoinedAtUtc.Offset);
        Assert.Equal(Now.ToUniversalTime(), student.JoinedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingNameOnRejoinPreservesTheLastKnownName(string? displayName)
    {
        var participant = SessionParticipant.Join(Guid.NewGuid(), "student", ParticipantRole.Student, Now, "Original");

        participant.UpdateDisplayName(displayName);
        Assert.Equal("Original", participant.DisplayName);
        participant.UpdateDisplayName("  Renamed  ");
        Assert.Equal("Renamed", participant.DisplayName);
    }

    [Fact]
    public void ParticipantRejectsMissingSessionUnknownRoleAndOverlongIdentity()
    {
        Assert.Throws<ArgumentException>(() => SessionParticipant.Join(Guid.Empty, "student", ParticipantRole.Student, Now));
        Assert.Throws<ArgumentException>(() => SessionParticipant.Join(Guid.NewGuid(), "student", (ParticipantRole)99, Now));
        Assert.Throws<ArgumentException>(() => SessionParticipant.Join(Guid.NewGuid(), new string('x', 101), ParticipantRole.Student, Now));
        Assert.Equal(100, SessionParticipant.Join(Guid.NewGuid(), new string('x', 100), ParticipantRole.Student, Now).SubjectId.Length);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("ABCD234", false)]
    [InlineData("ABCD23456", false)]
    [InlineData("ABCD2340", false)]
    [InlineData("ABCD2341", false)]
    [InlineData("ABCD234I", false)]
    [InlineData("ABCD234O", false)]
    [InlineData("abcd2345", true)]
    [InlineData("  ABCD2345  ", true)]
    public void SessionCodeValidationMatchesConstruction(string? value, bool valid)
    {
        Assert.Equal(valid, SessionCode.IsValid(value));
        if (valid)
        {
            var code = new SessionCode(value!);
            Assert.Equal("ABCD2345", code.Value);
            Assert.Equal(code.Value, code.ToString());
        }
        else Assert.ThrowsAny<ArgumentException>(() => new SessionCode(value!));
    }

    [Theory]
    [InlineData("question")]
    [InlineData("answer")]
    [InlineData("teacher")]
    public void InvalidFaqAnswerCannotPartiallyChangeTheStoredQuestion(string invalidField)
    {
        var faq = FaqAnswer.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Original", [Guid.NewGuid(), Guid.NewGuid()], Now);

        Assert.ThrowsAny<ArgumentException>(() => faq.RecordAnswer(invalidField == "answer" ? " " : "Answer",
            invalidField == "teacher" ? " " : "teacher", Now, invalidField == "question" ? " " : "Revised"));

        Assert.Equal("Original", faq.Question);
        Assert.Null(faq.Answer);
        Assert.Null(faq.AnsweredAt);
        Assert.Empty(faq.GetDomainEvents());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TeacherFaqRequiresAPositiveGeneration(int generation)
    {
        Assert.Throws<ArgumentException>(() => FaqAnswer.CreateAnswered(Guid.NewGuid(), generation, "Question", "Answer", "teacher", Now));
    }

    [Fact]
    public void FaqSourceIdsAreDistinctSortedAndIndependentOfTheInputArray()
    {
        var first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid[] sources = [second, first, second];
        var faq = FaqAnswer.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "  Question  ", sources, Now);
        sources[0] = Guid.Empty;

        Assert.Equal(new[] { first, second }, faq.HelpRequestIds);
        Assert.Equal("Question", faq.Question);
        Assert.Equal(TimeSpan.Zero, faq.CreatedAt.Offset);
        Assert.Throws<ArgumentException>(() => FaqAnswer.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Question", [first, Guid.Empty], Now));
    }
}
