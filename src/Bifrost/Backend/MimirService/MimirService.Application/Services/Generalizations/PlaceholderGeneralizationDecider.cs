namespace MimirService.Application.Services.Generalizations;

// Placeholder until the Gemini decider replaces it: pairs a new question with a waiting one that has the same text,
// ignoring case and surrounding whitespace, so different questions still go through the ordinary queue.
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
