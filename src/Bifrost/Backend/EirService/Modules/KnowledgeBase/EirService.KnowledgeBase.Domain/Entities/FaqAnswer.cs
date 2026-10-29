using EirService.KnowledgeBase.Domain.DomainEvents;
using Shared.Domain.Primitives;

namespace EirService.KnowledgeBase.Domain.Entities;

public sealed class FaqAnswer : AggregateRoot<Guid>
{
    public const int MinSourceCount = 2;
    public const int MaxSourceCount = 100;
    public const int MaxTextLength = 4000;

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
        if (id == Guid.Empty || sessionId == Guid.Empty || generation < 1 ||
            ids.Length < MinSourceCount || ids.Length > MaxSourceCount || ids.Contains(Guid.Empty) || ids.Contains(id))
            throw new ArgumentException("A generalization requires a session, generation and 2-100 distinct source help requests.");
        return new FaqAnswer
        {
            Id = id, SessionId = sessionId, Generation = generation, Question = Text(question),
            helpRequestIds = ids, CreatedAt = now.ToUniversalTime()
        };
    }

    public static FaqAnswer CreateAnswered(Guid sessionId, int generation, string question,
        string answer, string teacherId, DateTimeOffset now)
    {
        if (sessionId == Guid.Empty || generation < 1)
            throw new ArgumentException("A FAQ requires a session and generation.");
        var faq = new FaqAnswer
        {
            Id = Guid.NewGuid(), SessionId = sessionId, Generation = generation,
            Question = Text(question), CreatedAt = now.ToUniversalTime()
        };
        faq.RecordAnswer(answer, teacherId, now);
        return faq;
    }

    public void RecordAnswer(string answer, string teacherId, DateTimeOffset now, string? question = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teacherId);
        var text = Text(answer);
        var questionText = question is null ? Question : Text(question);
        if (Answer is not null)
        {
            if (Answer == text && AnsweredByTeacherId == teacherId && Question == questionText) return;
            throw new InvalidOperationException("This FAQ has already been answered.");
        }
        Question = questionText;
        Answer = text;
        AnsweredByTeacherId = teacherId;
        AnsweredAt = now.ToUniversalTime();
        RaiseDomainEvent(new FaqAnswerRecordedDomainEvent(this));
    }

    private static string Text(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > MaxTextLength)
            throw new ArgumentException("FAQ text must contain between 1 and 4000 characters.");
        return value.Trim();
    }
}
