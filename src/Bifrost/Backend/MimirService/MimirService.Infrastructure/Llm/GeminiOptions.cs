namespace MimirService.Infrastructure.Llm;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;

    public string[] Models { get; set; } = [];

    public double Temperature { get; set; } = 0.2;
}
