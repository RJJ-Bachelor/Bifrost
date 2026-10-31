using System.Text.Json;
using MimirService.Application.Services.Llm;

namespace MimirService.Test;

internal sealed class FakeLlmClient : ILlmClient
{
    public string Answer { get; init; } = """{"decision":"standalone","candidateId":null,"faqQuestion":null}""";

    public bool Unavailable { get; init; }

    public List<string> Prompts { get; } = [];

    public Task<string> GenerateAsync(string systemInstruction, string prompt, JsonElement responseSchema, CancellationToken cancellationToken)
    {
        Prompts.Add(prompt);
        return Unavailable
            ? throw new LlmUnavailableException("All configured LLM models failed.")
            : Task.FromResult(Answer);
    }
}
