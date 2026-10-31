using System.Text.Json;
using MimirService.Application.Services.Generalizations;

namespace MimirService.Evals;

internal enum EvalOutcome { Correct, WrongMerge, Missed, NoModelResponded }

internal sealed record EvalResult(
    EvalCase Case,
    EvalOutcome Outcome,
    string Decision,
    string? CandidateId,
    string? FaqQuestion,
    string? Answer,
    bool Called,
    bool InvalidAnswer,
    bool UnwantedFaq,
    IReadOnlyList<string> MustNotContainHits)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Expected => Describe(Case.Expected.Decision, Case.Expected.CandidateId);

    public string Got => Outcome == EvalOutcome.NoModelResponded ? "no answer" : Describe(Decision, CandidateId);

    public bool Failed => Outcome != EvalOutcome.Correct || Problems().Count > 0;

    // candidateId is the case's own id (r1, g1) for the candidate the decider chose.
    public static EvalResult Score(EvalCase evalCase, GeneralizationDecision decision, string? candidateId, RecordingLlmClient llm)
    {
        var expected = evalCase.Expected;
        var decisionName = decision.Type.ToString().ToLowerInvariant();
        var raw = Parse(llm.Answer);

        var outcome = llm.Unavailable ? EvalOutcome.NoModelResponded
            : decisionName == expected.Decision && candidateId == expected.CandidateId ? EvalOutcome.Correct
            : decision.Type == GeneralizationDecisionType.Standalone ? EvalOutcome.Missed
            : EvalOutcome.WrongMerge;

        // The decider turns invalid answers into standalone, so only the raw answer shows them.
        var invalidAnswer = llm.Answer is not null
            && decision.Type == GeneralizationDecisionType.Standalone
            && raw?.Decision != "standalone";
        var unwantedFaq = raw is { Decision: "join" or "standalone" } && !string.IsNullOrWhiteSpace(raw.FaqQuestion);
        IReadOnlyList<string> hits = outcome == EvalOutcome.Correct && decision.FaqQuestion is { } faq && expected.Faq is { } expectedFaq
            ? expectedFaq.MustNotContain.Where(text => faq.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList()
            : [];

        return new EvalResult(evalCase, outcome, decisionName, candidateId, decision.FaqQuestion, llm.Answer,
            llm.Answer is not null || llm.Unavailable, invalidAnswer, unwantedFaq, hits);
    }

    // Problems besides the decision itself, which Expected and Got show.
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (InvalidAnswer)
            problems.Add($"invalid answer {Answer}");
        if (UnwantedFaq)
            problems.Add("FAQ question on join or standalone");
        if (MustNotContainHits.Count > 0)
            problems.Add($"FAQ contains {string.Join(", ", MustNotContainHits)}: {FaqQuestion}");
        return problems;
    }

    private static string Describe(string decision, string? candidateId) =>
        candidateId is null ? decision : $"{decision} {candidateId}";

    private static RawAnswer? Parse(string? answer)
    {
        if (answer is null) return null;
        try
        {
            return JsonSerializer.Deserialize<RawAnswer>(answer, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record RawAnswer(string? Decision, string? FaqQuestion);
}
