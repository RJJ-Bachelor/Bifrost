namespace MimirService.Evals;

// The fields of generalization-cases.json that the harness uses. README.md describes each field.
// sessionTitle is left out, because Mimir does not receive the session title.
internal sealed record EvalCase(
    string Id,
    string Category,
    IReadOnlyList<EvalCandidate> Candidates,
    string NewQuestion,
    EvalExpectation Expected);

// A request has Text and a generalization has FaqQuestion.
internal sealed record EvalCandidate(string Id, string Type, string? Text, string? FaqQuestion);

internal sealed record EvalExpectation(string Decision, string? CandidateId, EvalFaq? Faq);

internal sealed record EvalFaq(string Example, IReadOnlyList<string> MustNotContain);
