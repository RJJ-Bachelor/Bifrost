namespace MimirService.Application.Services.Generalizations;

// Fake decider without an LLM (the default): pairs only questions with the same text.
public sealed class PlaceholderGeneralizationDecider : IGeneralizationDecider
{
    public const string Question = "Dette er et generaliseret spørgsmål.";

    public Task<GeneralizationDecision> DecideAsync(
        string newQuestion,
        IReadOnlyList<GeneralizationCandidate> candidates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var match = candidates.FirstOrDefault(candidate => candidate.Type == GeneralizationCandidateType.Request &&
            string.Equals(candidate.Text.Trim(), newQuestion.Trim(), StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match is null
            ? GeneralizationDecision.Standalone
            : new GeneralizationDecision(GeneralizationDecisionType.Pair, match.Id, Question));
    }
}
