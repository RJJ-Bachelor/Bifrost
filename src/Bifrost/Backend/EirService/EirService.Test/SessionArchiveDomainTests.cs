using EirService.Sessions.Domain.DomainEvents;
using EirService.Sessions.Domain.Entities;
using EirService.Sessions.Domain.ValueObjects;

namespace EirService.Test;

public sealed class SessionArchiveDomainTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ArchivePreservesSessionAndRecordsGenerationBoundaries()
    {
        var session = Session.Create("Algorithms", new SessionCode("ABCD2345"), new SessionCode("EFGH6789"), "teacher", null, CreatedAt);
        var archivedAt = CreatedAt.AddHours(1).ToOffset(TimeSpan.FromHours(2));
        var archive = session.Archive(1, archivedAt);
        Assert.Equal(2, session.CurrentGeneration);
        Assert.Equal("ABCD2345", session.Code.Value);
        Assert.Equal("EFGH6789", session.TeacherCode.Value);
        Assert.Null(session.LastClearedAtUtc);
        Assert.Equal(archivedAt.ToUniversalTime(), session.CurrentGenerationStartedAtUtc);
        Assert.Equal(session.Id, archive.SessionId);
        Assert.Equal("Algorithms", archive.Title);
        Assert.Equal(1, archive.Generation);
        Assert.Equal(CreatedAt, archive.StartedAtUtc);
        Assert.Equal(archivedAt.ToUniversalTime(), archive.ArchivedAtUtc);
        Assert.Same(archive, Assert.Single(session.Archives));
        var domainEvent = Assert.IsType<SessionArchivedDomainEvent>(Assert.Single(session.GetDomainEvents()));
        Assert.Equal(session.Id, domainEvent.SessionId);
        Assert.Equal(1, domainEvent.PreviousGeneration);
        Assert.Equal(2, domainEvent.CurrentGeneration);
        Assert.Equal(archive.ArchivedAtUtc, domainEvent.ArchivedAtUtc);
        Assert.NotEqual(Guid.Empty, domainEvent.Id);
        Assert.Throws<InvalidOperationException>(() => session.Archive(1, CreatedAt.AddHours(2)));
        Assert.Single(session.Archives);
        Assert.Single(session.GetDomainEvents());
        Assert.Equal(2, session.CurrentGeneration);
    }

    [Fact]
    public void ClearDoesNotCreateArchiveAndNextArchiveStartsAtClearTime()
    {
        var session = Session.Create("Title", new SessionCode("ABCD2345"), new SessionCode("EFGH6789"), "teacher", null, CreatedAt);
        session.Clear(1, CreatedAt.AddMinutes(30));
        Assert.Empty(session.Archives);
        var first = session.Archive(2, CreatedAt.AddHours(1));
        var second = session.Archive(3, CreatedAt.AddHours(2));
        Assert.Equal(2, first.Generation);
        Assert.Equal(CreatedAt.AddMinutes(30), first.StartedAtUtc);
        Assert.Equal(3, second.Generation);
        Assert.Equal(first.ArchivedAtUtc, second.StartedAtUtc);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(4, session.CurrentGeneration);
    }
}
