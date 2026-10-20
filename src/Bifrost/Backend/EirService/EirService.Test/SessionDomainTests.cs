using EirService.Sessions.Domain.DomainEvents;
using EirService.Sessions.Domain.Entities;
using EirService.Sessions.Domain.ValueObjects;

namespace EirService.Test;

public sealed class SessionDomainTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ClearPreservesSessionMetadataAndRejectsStaleGeneration()
    {
        var session = Session.Create("  Algorithms  ", new SessionCode("abcd2345"), new SessionCode("efgh6789"), "provider|opaque-subject", null, CreatedAt);
        var id = session.Id;
        Assert.Empty(session.GetDomainEvents());
        session.Clear(1, CreatedAt.AddHours(1));
        Assert.Equal(id, session.Id);
        Assert.Equal("Algorithms", session.Title);
        Assert.Equal("ABCD2345", session.Code.Value);
        Assert.Equal("EFGH6789", session.TeacherCode.Value);
        Assert.Equal("provider|opaque-subject", session.CreatedByTeacherId);
        Assert.Null(session.CreatedByDisplayName);
        Assert.Equal(CreatedAt, session.CreatedAtUtc);
        Assert.Equal(2, session.CurrentGeneration);
        Assert.Equal(CreatedAt.AddHours(1), session.LastClearedAtUtc);
        var domainEvent = Assert.IsType<SessionClearedDomainEvent>(Assert.Single(session.GetDomainEvents()));
        Assert.NotEqual(Guid.Empty, domainEvent.Id);
        Assert.Equal(session.Id, domainEvent.SessionId);
        Assert.Equal(1, domainEvent.PreviousGeneration);
        Assert.Equal(2, domainEvent.CurrentGeneration);
        Assert.Equal(session.LastClearedAtUtc, domainEvent.ClearedAtUtc);
        Assert.Throws<InvalidOperationException>(() => session.Clear(1, CreatedAt.AddHours(2)));
        Assert.Equal(2, session.CurrentGeneration);
        Assert.Equal(CreatedAt.AddHours(1), session.LastClearedAtUtc);
        Assert.Single(session.GetDomainEvents());
    }

    [Fact]
    public void DeleteRaisesDomainEventForTheWholeSessionAtItsCurrentGeneration()
    {
        var session = Session.Create("Title", new SessionCode("ABCD2345"), new SessionCode("EFGH6789"), "teacher", null, CreatedAt);
        session.Clear(1, CreatedAt.AddHours(1));
        session.ClearDomainEvents();
        var deletedAt = CreatedAt.AddHours(2).ToOffset(TimeSpan.FromHours(2));
        session.Delete(deletedAt);
        var deleted = Assert.IsType<SessionDeletedDomainEvent>(Assert.Single(session.GetDomainEvents()));
        Assert.Equal(session.Id, deleted.SessionId);
        Assert.Equal(2, deleted.Generation);
        Assert.Equal(deletedAt.ToUniversalTime(), deleted.DeletedAtUtc);
        Assert.Equal(TimeSpan.Zero, deleted.DeletedAtUtc.Offset);
        Assert.NotEqual(Guid.Empty, deleted.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsEmptyTitleAndCreator(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => Session.Create(value!, new SessionCode("ABCD2345"), new SessionCode("EFGH6789"), "teacher", null, CreatedAt));
        Assert.ThrowsAny<ArgumentException>(() => Session.Create("Title", new SessionCode("ABCD2345"), new SessionCode("EFGH6789"), value!, null, CreatedAt));
    }

    [Fact]
    public void TitleLengthIsValidatedAfterTrimming()
    {
        Assert.Equal(200, Session.Create(" " + new string('x', 200) + " ", new SessionCode("ABCD2345"), new SessionCode("EFGH6789"), "teacher", null, CreatedAt).Title.Length);
        Assert.Throws<ArgumentException>(() => Session.Create(new string('x', 201), new SessionCode("ABCD2345"), new SessionCode("EFGH6789"), "teacher", null, CreatedAt));
    }

    [Fact]
    public void StudentAndTeacherCodesMustBeDifferent()
    {
        Assert.Throws<ArgumentException>(() => Session.Create("Title", new SessionCode("ABCD2345"),
            new SessionCode("abcd2345"), "teacher", null, CreatedAt));
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
