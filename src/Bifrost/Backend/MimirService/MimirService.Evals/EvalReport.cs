using System.Text;

namespace MimirService.Evals;

internal static class EvalReport
{
    public static void WriteToConsole(IReadOnlyList<EvalResult> results)
    {
        Console.WriteLine();
        Console.WriteLine("Summary");
        foreach (var (name, value) in Summary(results))
            Console.WriteLine($"  {name,-32} {value}");

        Console.WriteLine();
        Console.WriteLine("By category");
        foreach (var category in results.GroupBy(r => r.Case.Category))
            Console.WriteLine($"  {category.Key,-20} {CorrectRatio(category)}");

        Console.WriteLine();
        Console.WriteLine("Failures");
        foreach (var result in results.Where(r => r.Failed))
            Console.WriteLine($"  [{result.Case.Category}] {result.Case.Id}: expected {result.Expected}, got {result.Got}"
                + string.Concat(result.Problems().Select(problem => "; " + problem)));

        Console.WriteLine();
        Console.WriteLine("FAQ questions for manual review");
        foreach (var result in CorrectPairs(results))
        {
            Console.WriteLine($"  {result.Case.Id}");
            Console.WriteLine($"    model:    {result.FaqQuestion}");
            Console.WriteLine($"    example:  {result.Case.Expected.Faq?.Example}");
        }
    }

    public static string WriteMarkdown(IReadOnlyList<EvalResult> results, string model, DateTimeOffset startedAt, string directory)
    {
        var markdown = new StringBuilder();
        markdown.AppendLine($"# Eval: {model}");
        markdown.AppendLine();
        markdown.AppendLine($"{startedAt:yyyy-MM-dd HH:mm}, {results.Count} cases.");

        markdown.AppendLine();
        markdown.AppendLine("## Summary");
        markdown.AppendLine();
        markdown.AppendLine("| Metric | Result |");
        markdown.AppendLine("|---|---|");
        foreach (var (name, value) in Summary(results))
            markdown.AppendLine($"| {name} | {value} |");

        markdown.AppendLine();
        markdown.AppendLine("## By category");
        markdown.AppendLine();
        markdown.AppendLine("| Category | Correct | Wrong merges | Missed | No model responded |");
        markdown.AppendLine("|---|---|---|---|---|");
        foreach (var category in results.GroupBy(r => r.Case.Category))
            markdown.AppendLine($"| {category.Key} | {CorrectRatio(category)} | {Count(category, EvalOutcome.WrongMerge)} " +
                $"| {Count(category, EvalOutcome.Missed)} | {Count(category, EvalOutcome.NoModelResponded)} |");

        markdown.AppendLine();
        markdown.AppendLine("## Failures");
        markdown.AppendLine();
        markdown.AppendLine("| Case | Category | Expected | Got | Problems |");
        markdown.AppendLine("|---|---|---|---|---|");
        foreach (var result in results.Where(r => r.Failed))
            markdown.AppendLine($"| {result.Case.Id} | {result.Case.Category} | {result.Expected} | {result.Got} " +
                $"| {Cell(string.Join("; ", result.Problems()))} |");

        markdown.AppendLine();
        markdown.AppendLine("## FAQ questions for manual review");
        markdown.AppendLine();
        markdown.AppendLine("| Case | Model | Example |");
        markdown.AppendLine("|---|---|---|");
        foreach (var result in CorrectPairs(results))
            markdown.AppendLine($"| {result.Case.Id} | {Cell(result.FaqQuestion)} | {Cell(result.Case.Expected.Faq?.Example)} |");

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{startedAt:yyyy-MM-dd-HHmm}-{model}.md");
        File.WriteAllText(path, markdown.ToString());
        return path;
    }

    private static IEnumerable<(string Name, string Value)> Summary(IReadOnlyList<EvalResult> results)
    {
        var answered = results.Where(r => r.Outcome != EvalOutcome.NoModelResponded).ToList();
        var pairs = CorrectPairs(results);
        return
        [
            ("Correct", Ratio(answered.Count(r => r.Outcome == EvalOutcome.Correct), answered.Count)),
            ("Wrong merges", Count(results, EvalOutcome.WrongMerge).ToString()),
            ("Missed", Count(results, EvalOutcome.Missed).ToString()),
            ("Invalid answers", results.Count(r => r.InvalidAnswer).ToString()),
            ("No model responded", Count(results, EvalOutcome.NoModelResponded).ToString()),
            ("FAQ without mustNotContain hits", Ratio(pairs.Count(r => r.MustNotContainHits.Count == 0), pairs.Count)),
            ("FAQ on join/standalone", results.Count(r => r.UnwantedFaq).ToString())
        ];
    }

    private static List<EvalResult> CorrectPairs(IEnumerable<EvalResult> results) =>
        results.Where(r => r.Outcome == EvalOutcome.Correct && r.Decision == "pair").ToList();

    // Cases where no model responded are left out, so rate limits do not count as wrong decisions.
    private static string CorrectRatio(IEnumerable<EvalResult> results)
    {
        var answered = results.Where(r => r.Outcome != EvalOutcome.NoModelResponded).ToList();
        return Ratio(answered.Count(r => r.Outcome == EvalOutcome.Correct), answered.Count);
    }

    private static int Count(IEnumerable<EvalResult> results, EvalOutcome outcome) =>
        results.Count(r => r.Outcome == outcome);

    private static string Ratio(int part, int total) =>
        total == 0 ? $"{part}/{total}" : $"{part}/{total} ({100.0 * part / total:0.0} %)";

    // Student text and model answers may contain pipes and line breaks, which would break the table.
    private static string Cell(string? text) =>
        (text ?? "").ReplaceLineEndings(" ").Replace("|", "\\|");
}
