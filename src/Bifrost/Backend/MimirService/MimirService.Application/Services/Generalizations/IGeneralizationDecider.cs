namespace MimirService.Application.Services.Generalizations;

public interface IGeneralizationDecider
{
    Task<GeneralizationDecision> DecideAsync(
        string newQuestion,
        IReadOnlyList<GeneralizationCandidate> candidates,
        CancellationToken cancellationToken);
}
