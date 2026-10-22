using System.Text.Json;
using MimirService.Application.Services.Llm;

namespace EirService.Test;

internal sealed class FakeLlmClient : ILlmClient
{
    public Task<string> GenerateAsync(string systemInstruction, string prompt, JsonElement responseSchema, CancellationToken cancellationToken) =>
        Task.FromResult("""{"decision":"standalone","candidateId":null,"faqQuestion":null}""");
}
