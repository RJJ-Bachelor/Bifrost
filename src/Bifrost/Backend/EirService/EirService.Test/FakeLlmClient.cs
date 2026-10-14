using MimirService.Application.Services.Llm;

namespace EirService.Test;

internal sealed class FakeLlmClient : ILlmClient
{
    public Task<string> GenerateAsync(string systemInstruction, string prompt, CancellationToken cancellationToken) =>
        Task.FromResult("Generalized answer");
}
