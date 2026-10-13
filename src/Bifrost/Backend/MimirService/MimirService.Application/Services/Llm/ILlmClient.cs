namespace MimirService.Application.Services.Llm;

public interface ILlmClient
{
    Task<string> GenerateAsync(
        string systemInstruction,
        string prompt,
        CancellationToken cancellationToken);
}
