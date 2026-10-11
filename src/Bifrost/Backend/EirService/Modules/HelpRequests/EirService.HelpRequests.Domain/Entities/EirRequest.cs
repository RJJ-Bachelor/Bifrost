namespace EirService.HelpRequests.Domain.Entities;

public sealed class EirRequest
{
    private EirRequest()
    {
    }

    public EirRequest(string id, string userId, string message, Guid? sessionId = null, int? generation = null)
    {
        Id = id;
        UserId = userId;
        if ((sessionId is null) != (generation is null) || sessionId == Guid.Empty || generation <= 0)
            throw new ArgumentException("Session id and a positive generation must be supplied together.");
        Message = message;
        SessionId = sessionId;
        Generation = generation;
    }

    public string Id { get; private set; } = null!;

    // Legacy rows have no recoverable owner; new requests always have a user id.
    public string? UserId { get; private set; }

    public Guid? SessionId { get; private set; }
    public int? Generation { get; private set; }

    public string Message { get; private set; } = null!;
}
