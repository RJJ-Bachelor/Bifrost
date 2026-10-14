namespace MimirService.Application.Services.Llm;

public sealed class LlmUnavailableException(string message) : Exception(message);
