using System.Text.Json;
using MimirService.Application.Services.Llm;

namespace MimirService.Evals;

// Keeps the model's raw answer, because GeneralizationDecider turns invalid answers into standalone.
internal sealed class RecordingLlmClient(ILlmClient inner) : ILlmClient
{
    public string? Answer { get; private set; }

    public bool Unavailable { get; private set; }

    public async Task<string> GenerateAsync(string systemInstruction, string prompt, JsonElement responseSchema, CancellationToken cancellationToken)
    {
        try
        {
            Answer = await inner.GenerateAsync(systemInstruction, prompt, responseSchema, cancellationToken);
            return Answer;
        }
        catch (LlmUnavailableException)
        {
            Unavailable = true;
            throw;
        }
    }
}
