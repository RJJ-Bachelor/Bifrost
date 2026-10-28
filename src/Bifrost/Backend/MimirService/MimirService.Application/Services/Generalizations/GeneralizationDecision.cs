namespace MimirService.Application.Services.Generalizations;

public enum GeneralizationDecisionType { Standalone, Join, Pair }

public sealed record GeneralizationDecision(GeneralizationDecisionType Type, Guid? CandidateId, string? FaqQuestion)
{
    public static GeneralizationDecision Standalone { get; } = new(GeneralizationDecisionType.Standalone, null, null);
}
