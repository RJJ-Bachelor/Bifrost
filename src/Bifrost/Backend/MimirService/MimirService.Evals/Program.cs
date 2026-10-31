using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimirService.Application.Services.Generalizations;
using MimirService.Application.Services.Llm;
using MimirService.Evals;
using MimirService.Infrastructure;
using MimirService.Infrastructure.Llm;

// Runs generalization-cases.json against the real model. See README.md.
Console.OutputEncoding = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    // Development makes the host read the Gemini key from user secrets.
    EnvironmentName = Environments.Development
});
// The key is the AppHost's user secret, which Aspire passes to Mimir as Gemini:ApiKey.
builder.Configuration["Gemini:ApiKey"] ??= builder.Configuration["Parameters:gemini-api-key"];
builder.Services.AddGeminiLlmClient(builder.Configuration);
var delay = TimeSpan.FromSeconds(builder.Configuration.GetValue("Eval:DelaySeconds", 0.0));
using var host = builder.Build();

GeminiOptions gemini;
try
{
    gemini = host.Services.GetRequiredService<IOptions<GeminiOptions>>().Value;
}
catch (OptionsValidationException exception)
{
    Console.Error.WriteLine(exception.Message + " See README.md.");
    return 1;
}

var llmClient = host.Services.GetRequiredService<ILlmClient>();
var deciderLogger = host.Services.GetRequiredService<ILogger<GeneralizationDecider>>();
var cases = JsonSerializer.Deserialize<List<EvalCase>>(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "generalization-cases.json")),
    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

var startedAt = DateTimeOffset.Now;
Console.WriteLine($"Model {string.Join(", ", gemini.Models)}, {cases.Count} cases");
var results = new List<EvalResult>();
var retry = true;
foreach (var evalCase in cases)
{
    var result = await RunAsync(evalCase);
    if (result.Outcome == EvalOutcome.NoModelResponded && retry)
    {
        // The free tier allows 15 calls a minute per model, and Gemini asks to wait about 45 seconds.
        Console.Write($" no answer for {evalCase.Id}, retrying in 60 s ");
        await Task.Delay(TimeSpan.FromSeconds(60));
        result = await RunAsync(evalCase);
        // A second failure usually means the daily quota is used up, so later cases are not retried.
        retry = result.Outcome != EvalOutcome.NoModelResponded;
    }
    results.Add(result);
    Console.Write('.');
    if (result.Called) await Task.Delay(delay);
}
Console.WriteLine();

EvalReport.WriteToConsole(results);

var evalsDirectory = typeof(EvalReport).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "EvalsDirectory").Value!;
var reportPath = EvalReport.WriteMarkdown(results, string.Join("+", gemini.Models), startedAt,
    Path.Combine(evalsDirectory, "results"));
Console.WriteLine();
Console.WriteLine($"Report: {reportPath}");

return 0;

async Task<EvalResult> RunAsync(EvalCase evalCase)
{
    var recorder = new RecordingLlmClient(llmClient);
    var decider = new GeneralizationDecider(recorder, deciderLogger);

    // The decider gets GUIDs like in Mimir, so its choice is mapped back to the case's own ids.
    var caseIds = new Dictionary<Guid, string>();
    var candidates = new List<GeneralizationCandidate>();
    foreach (var candidate in evalCase.Candidates)
    {
        var id = Guid.NewGuid();
        caseIds[id] = candidate.Id;
        candidates.Add(new GeneralizationCandidate(id, ToCandidateType(candidate.Type),
            candidate.Text ?? candidate.FaqQuestion
                ?? throw new InvalidDataException($"Candidate {candidate.Id} in {evalCase.Id} has no text.")));
    }

    var decision = await decider.DecideAsync(evalCase.NewQuestion, candidates, CancellationToken.None);
    return EvalResult.Score(evalCase, decision, decision.CandidateId is { } chosen ? caseIds[chosen] : null, recorder);
}

static GeneralizationCandidateType ToCandidateType(string type) => type switch
{
    "request" => GeneralizationCandidateType.Request,
    "generalization" => GeneralizationCandidateType.Generalization,
    _ => throw new InvalidDataException($"Unknown candidate type '{type}'.")
};
