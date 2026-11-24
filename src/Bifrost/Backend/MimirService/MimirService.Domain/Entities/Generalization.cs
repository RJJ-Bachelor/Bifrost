using Shared.Domain.Primitives;
namespace MimirService.Domain.Entities;

public sealed class Generalization : AggregateRoot<Guid>
{
    public const int MaxQuestionLength = 4000;

    private Generalization()
    {
    }

    private Generalization(Guid id, Guid sessionId, int generation, string question, DateTimeOffset createdAt)
    {
        Id = id;
        SessionId = sessionId;
        Generation = generation;
        Question = question;
        CreatedAt = createdAt;
    }

    public Guid SessionId { get; private set; }

    public int Generation { get; private set; }

    public string Question { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Generalization Create(Guid id, Guid sessionId, int generation, string question, DateTimeOffset now)
    {
        if (id == Guid.Empty) throw new ArgumentException("An id is required.", nameof(id));
        if (sessionId == Guid.Empty || generation < 1)
            throw new ArgumentException("A session and positive generation are required.");

        return new Generalization(id, sessionId, generation, ValidQuestion(question), now.ToUniversalTime());
    }

    // The teacher can rephrase the FAQ question when answering it.
    public void ChangeQuestion(string question) => Question = ValidQuestion(question);

    private static string ValidQuestion(string question)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Trim().Length > MaxQuestionLength)
            throw new ArgumentException("The question must contain between 1 and 4000 characters.", nameof(question));
        return question.Trim();
    }
}
