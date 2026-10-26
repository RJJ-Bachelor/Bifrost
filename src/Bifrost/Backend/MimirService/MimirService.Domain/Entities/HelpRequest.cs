using Shared.Domain.Primitives;
namespace MimirService.Domain.Entities;

public sealed class HelpRequest : AggregateRoot<Guid>
{
    private HelpRequest()
    {
    }

    private HelpRequest(Guid id, Guid sessionId, int generation, string message)
    {
        Id = id;
        SessionId = sessionId;
        Generation = generation;
        Message = message;
        ReceivedAt = DateTimeOffset.UtcNow;
    }

    public Guid SessionId { get; private set; }

    public int Generation { get; private set; }

    public string Message { get; private set; } = null!;

    public DateTimeOffset ReceivedAt { get; private set; }

    public Guid? GeneralizationId { get; private set; }

    public static HelpRequest Create(Guid id, Guid sessionId, int generation, string message)
    {
        if (id == Guid.Empty) throw new ArgumentException("An id is required.", nameof(id));
        if (sessionId == Guid.Empty || generation < 1)
            throw new ArgumentException("A session and positive generation are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new HelpRequest(id, sessionId, generation, message);
    }

    public void LinkToGeneralization(Generalization generalization)
    {
        ArgumentNullException.ThrowIfNull(generalization);
        if (generalization.SessionId != SessionId || generalization.Generation != Generation)
            throw new ArgumentException("The generalization belongs to another session or generation.", nameof(generalization));
        if (GeneralizationId is not null)
            throw new InvalidOperationException("The help request already belongs to a generalization.");

        GeneralizationId = generalization.Id;
    }
}
