using Shared.Domain.Primitives;

namespace EirService.KnowledgeBase.Domain.Entities;

public sealed class FaqAnswer : AggregateRoot<Guid>
{
    private Guid[] helpRequestIds = [];
    private FaqAnswer() { }
    public Guid SessionId { get; private set; }
    public int Generation { get; private set; }
    public string Question { get; private set; } = null!;
    public string? Answer { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AnsweredAt { get; private set; }
    public string? AnsweredByTeacherId { get; private set; }
    public IReadOnlyList<Guid> HelpRequestIds => Array.AsReadOnly(helpRequestIds);

    public static FaqAnswer Create(Guid id, Guid sessionId, int generation, string question,
        IEnumerable<Guid> sourceIds, DateTimeOffset now)
    {
        var ids = sourceIds.Distinct().Order().ToArray();
        if (id == Guid.Empty || sessionId == Guid.Empty || generation < 1 || ids.Length < 2 || ids.Contains(Guid.Empty))
            throw new ArgumentException("A generalization requires a session, generation and at least two distinct help requests.");
        return new FaqAnswer
        {
            Id = id, SessionId = sessionId, Generation = generation, Question = Text(question),
            helpRequestIds = ids, CreatedAt = now.ToUniversalTime()
        };
    }

    public void RecordAnswer(string answer, string teacherId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teacherId);
        var text = Text(answer);
        if (Answer is not null)
        {
            if (Answer == text && AnsweredByTeacherId == teacherId) return;
            throw new InvalidOperationException("This FAQ has already been answered.");
        }
        Answer = text;
        AnsweredByTeacherId = teacherId;
        AnsweredAt = now.ToUniversalTime();
    }

    private static string Text(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 4000)
            throw new ArgumentException("FAQ text must contain between 1 and 4000 characters.");
        return value.Trim();
    }
}
