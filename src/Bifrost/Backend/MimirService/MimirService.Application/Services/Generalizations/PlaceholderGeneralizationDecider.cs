namespace MimirService.Application.Services.Generalizations;

// Placeholder until the Gemini decider replaces it: pairs every new question with a waiting one.
public sealed class PlaceholderGeneralizationDecider : IGeneralizationDecider
{
    public const string Question = "Dette er et generaliseret spørgsmål.";

    public Task<GeneralizationDecision> DecideAsync(
        string newQuestion,
        IReadOnlyList<GeneralizationCandidate> candidates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var waiting = candidates.FirstOrDefault(candidate => candidate.Type == GeneralizationCandidateType.Request);
        return Task.FromResult(waiting is null
            ? GeneralizationDecision.Standalone
            : new GeneralizationDecision(GeneralizationDecisionType.Pair, waiting.Id, Question));
    }
}
