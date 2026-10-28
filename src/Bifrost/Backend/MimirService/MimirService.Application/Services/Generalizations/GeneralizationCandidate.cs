namespace MimirService.Application.Services.Generalizations;

public enum GeneralizationCandidateType { Request, Generalization }

// Text is the student's question for a request and the FAQ question for a generalization.
public sealed record GeneralizationCandidate(Guid Id, GeneralizationCandidateType Type, string Text);
