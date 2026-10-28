using MimirService.Application.Services.Generalizations;

namespace MimirService.Test;

public sealed class PlaceholderGeneralizationDeciderTests
{
    private readonly PlaceholderGeneralizationDecider decider = new();

    [Fact]
    public async Task NoCandidatesIsStandalone()
    {
        var decision = await decider.DecideAsync("Help me", [], CancellationToken.None);
        Assert.Equal(GeneralizationDecision.Standalone, decision);
    }

    [Fact]
    public async Task PairsWithTheFirstWaitingRequestWithTheSameText()
    {
        var other = new GeneralizationCandidate(Guid.NewGuid(), GeneralizationCandidateType.Request, "Something else");
        var first = new GeneralizationCandidate(Guid.NewGuid(), GeneralizationCandidateType.Request, "Help me");
        var second = new GeneralizationCandidate(Guid.NewGuid(), GeneralizationCandidateType.Request, "Help me");
        var generalization = new GeneralizationCandidate(Guid.NewGuid(), GeneralizationCandidateType.Generalization, "Help me");
        var decision = await decider.DecideAsync("Help me", [generalization, other, first, second], CancellationToken.None);
        Assert.Equal(GeneralizationDecisionType.Pair, decision.Type);
        Assert.Equal(first.Id, decision.CandidateId);
        Assert.Equal(PlaceholderGeneralizationDecider.Question, decision.FaqQuestion);
    }

    [Fact]
    public async Task IgnoresCaseAndSurroundingWhitespace()
    {
        var waiting = new GeneralizationCandidate(Guid.NewGuid(), GeneralizationCandidateType.Request, "  help ME ");
        var decision = await decider.DecideAsync("Help me", [waiting], CancellationToken.None);
        Assert.Equal(GeneralizationDecisionType.Pair, decision.Type);
        Assert.Equal(waiting.Id, decision.CandidateId);
    }

    [Fact]
    public async Task DifferentTextIsStandalone()
    {
        var waiting = new GeneralizationCandidate(Guid.NewGuid(), GeneralizationCandidateType.Request, "Something else");
        var decision = await decider.DecideAsync("Help me", [waiting], CancellationToken.None);
        Assert.Equal(GeneralizationDecision.Standalone, decision);
    }

    [Fact]
    public async Task NeverJoinsAGeneralization()
    {
        var generalization = new GeneralizationCandidate(Guid.NewGuid(), GeneralizationCandidateType.Generalization, "Help me");
        var decision = await decider.DecideAsync("Help me", [generalization], CancellationToken.None);
        Assert.Equal(GeneralizationDecision.Standalone, decision);
    }
}
