using Shared.Domain.Primitives;
namespace EirService.HelpRequests.Domain.Entities;

public sealed class HelpRequest : AggregateRoot<Guid>
{
    private HelpRequest()
    {
    }

    public HelpRequest(Guid id, string userId, string message, Guid? sessionId = null, int? generation = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("An id is required.", nameof(id));
        Id = id;
        UserId = userId;
        if ((sessionId is null) != (generation is null) || sessionId == Guid.Empty || generation <= 0)
            throw new ArgumentException("Session id and a positive generation must be supplied together.");
        Message = message;
        SessionId = sessionId;
        Generation = generation;
    }


    // Legacy rows have no recoverable owner; new requests always have a user id.
    public string? UserId { get; private set; }

    public Guid? SessionId { get; private set; }
    public int? Generation { get; private set; }

    public string Message { get; private set; } = null!;
}
