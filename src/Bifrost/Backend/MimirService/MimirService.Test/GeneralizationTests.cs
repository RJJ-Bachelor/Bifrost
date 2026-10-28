using MimirService.Domain.Entities;

namespace MimirService.Test;

public sealed class GeneralizationTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    [Fact]
    public void CreateTrimsQuestionAndStoresUtcTime()
    {
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(1));
        var generalization = Generalization.Create(Guid.NewGuid(), SessionId, 1, "  How does recursion work?  ", now);
        Assert.Equal("How does recursion work?", generalization.Question);
        Assert.Equal(now, generalization.CreatedAt);
        Assert.Equal(TimeSpan.Zero, generalization.CreatedAt.Offset);
    }

    [Fact]
    public void CreateRejectsMissingIdSessionOrGeneration()
    {
        Assert.Throws<ArgumentException>(() => Generalization.Create(Guid.Empty, SessionId, 1, "Question", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => Generalization.Create(Guid.NewGuid(), Guid.Empty, 1, "Question", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => Generalization.Create(Guid.NewGuid(), SessionId, 0, "Question", DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsBlankQuestion(string question) =>
        Assert.Throws<ArgumentException>(() => Generalization.Create(Guid.NewGuid(), SessionId, 1, question, DateTimeOffset.UtcNow));

    [Fact]
    public void CreateRejectsTooLongQuestion() =>
        Assert.Throws<ArgumentException>(() => Generalization.Create(Guid.NewGuid(), SessionId, 1,
            new string('a', Generalization.MaxQuestionLength + 1), DateTimeOffset.UtcNow));

    [Fact]
    public void LinkingSetsTheGeneralizationId()
    {
        var request = HelpRequest.Create(Guid.NewGuid(), SessionId, 1, "Help me");
        var generalization = NewGeneralization(SessionId, 1);
        Assert.Null(request.GeneralizationId);
        request.LinkToGeneralization(generalization);
        Assert.Equal(generalization.Id, request.GeneralizationId);
    }

    [Fact]
    public void LinkingRejectsAnotherSessionOrGeneration()
    {
        var request = HelpRequest.Create(Guid.NewGuid(), SessionId, 1, "Help me");
        Assert.Throws<ArgumentException>(() => request.LinkToGeneralization(NewGeneralization(Guid.NewGuid(), 1)));
        Assert.Throws<ArgumentException>(() => request.LinkToGeneralization(NewGeneralization(SessionId, 2)));
        Assert.Null(request.GeneralizationId);
    }

    [Fact]
    public void RequestCanOnlyBelongToOneGeneralization()
    {
        var request = HelpRequest.Create(Guid.NewGuid(), SessionId, 1, "Help me");
        var first = NewGeneralization(SessionId, 1);
        request.LinkToGeneralization(first);
        Assert.Throws<InvalidOperationException>(() => request.LinkToGeneralization(NewGeneralization(SessionId, 1)));
        Assert.Equal(first.Id, request.GeneralizationId);
    }

    private static Generalization NewGeneralization(Guid sessionId, int generation) =>
        Generalization.Create(Guid.NewGuid(), sessionId, generation, "Question", DateTimeOffset.UtcNow);
}
