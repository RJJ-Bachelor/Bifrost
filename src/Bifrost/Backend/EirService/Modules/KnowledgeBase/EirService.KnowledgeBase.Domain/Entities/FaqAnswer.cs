namespace EirService.KnowledgeBase.Domain.Entities;

// Data model only. Persistence, FAQ suggestions and endpoints will be added later.
public sealed class FaqAnswer
{
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public int Generation { get; private set; }
    public string Question { get; private set; } = null!;
    public string? Answer { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
