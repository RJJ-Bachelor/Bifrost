using EirService.Sessions.Domain.Entities;
using EirService.Sessions.Domain.ValueObjects;

namespace EirService.Test;

public sealed class SessionDomainTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ClearPreservesSessionMetadataAndRejectsStaleGeneration()
    {
        var session = Session.Create("  Algorithms  ", new SessionCode("abcd2345"), "provider|opaque-subject", null, CreatedAt);
        var id = session.Id;
        session.Clear(1, CreatedAt.AddHours(1));
        Assert.Equal(id, session.Id);
        Assert.Equal("Algorithms", session.Title);
        Assert.Equal("ABCD2345", session.Code.Value);
        Assert.Equal("provider|opaque-subject", session.CreatedBySubjectId);
        Assert.Null(session.CreatedByDisplayName);
        Assert.Equal(CreatedAt, session.CreatedAtUtc);
        Assert.Equal(2, session.CurrentGeneration);
        Assert.Equal(CreatedAt.AddHours(1), session.LastClearedAtUtc);
        Assert.Throws<InvalidOperationException>(() => session.Clear(1, CreatedAt.AddHours(2)));
        Assert.Equal(2, session.CurrentGeneration);
        Assert.Equal(CreatedAt.AddHours(1), session.LastClearedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsEmptyTitleAndCreator(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => Session.Create(value!, new SessionCode("ABCD2345"), "teacher", null, CreatedAt));
        Assert.ThrowsAny<ArgumentException>(() => Session.Create("Title", new SessionCode("ABCD2345"), value!, null, CreatedAt));
    }

    [Fact]
    public void TitleLengthIsValidatedAfterTrimming()
    {
        Assert.Equal(200, Session.Create(" " + new string('x', 200) + " ", SessionCode.Generate(), "teacher", null, CreatedAt).Title.Length);
        Assert.Throws<ArgumentException>(() => Session.Create(new string('x', 201), SessionCode.Generate(), "teacher", null, CreatedAt));
    }

    [Fact]
    public void SessionCodeUsesValueEqualityAndValidatesAlphabet()
    {
        Assert.Equal(new SessionCode(" abcd2345 "), new SessionCode("ABCD2345"));
        Assert.Throws<ArgumentException>(() => new SessionCode("ABCD01IO"));
        Assert.Throws<ArgumentException>(() => new SessionCode("SHORT"));
        Assert.True(SessionCode.IsValid(SessionCode.Generate().Value));
    }
}
