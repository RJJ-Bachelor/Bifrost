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
    public async Task PairsWithTheFirstWaitingRequest()
    {
        var first = new GeneralizationCandidate(Guid.NewGuid(), GeneralizationCandidateType.Request, "First");
        var second = new GeneralizationCandidate(Guid.NewGuid(), GeneralizationCandidateType.Request, "Second");
        var generalization = new GeneralizationCandidate(Guid.NewGuid(), GeneralizationCandidateType.Generalization, "FAQ");
        var decision = await decider.DecideAsync("Help me", [generalization, first, second], CancellationToken.None);
        Assert.Equal(GeneralizationDecisionType.Pair, decision.Type);
        Assert.Equal(first.Id, decision.CandidateId);
        Assert.Equal(PlaceholderGeneralizationDecider.Question, decision.FaqQuestion);
    }

    [Fact]
    public async Task NeverJoinsAGeneralization()
    {
        var generalization = new GeneralizationCandidate(Guid.NewGuid(), GeneralizationCandidateType.Generalization, "FAQ");
        var decision = await decider.DecideAsync("Help me", [generalization], CancellationToken.None);
        Assert.Equal(GeneralizationDecision.Standalone, decision);
    }
}
