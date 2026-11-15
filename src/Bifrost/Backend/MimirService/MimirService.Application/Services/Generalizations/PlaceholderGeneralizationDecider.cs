namespace MimirService.Application.Services.Generalizations;

// Fake decider without an LLM (the default): joins or pairs only questions with the same text.
public sealed class PlaceholderGeneralizationDecider : IGeneralizationDecider
{
    public Task<GeneralizationDecision> DecideAsync(
        string newQuestion,
        IReadOnlyList<GeneralizationCandidate> candidates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var question = newQuestion.Trim();

        var generalization = candidates.FirstOrDefault(candidate =>
            candidate.Type == GeneralizationCandidateType.Generalization && SameText(candidate, question));
        if (generalization is not null)
            return Task.FromResult(new GeneralizationDecision(GeneralizationDecisionType.Join, generalization.Id, null));

        var request = candidates.FirstOrDefault(candidate =>
            candidate.Type == GeneralizationCandidateType.Request && SameText(candidate, question));
        return Task.FromResult(request is null
            ? GeneralizationDecision.Standalone
            : new GeneralizationDecision(GeneralizationDecisionType.Pair, request.Id, question));
    }

    private static bool SameText(GeneralizationCandidate candidate, string question) =>
        string.Equals(candidate.Text.Trim(), question, StringComparison.OrdinalIgnoreCase);
}
