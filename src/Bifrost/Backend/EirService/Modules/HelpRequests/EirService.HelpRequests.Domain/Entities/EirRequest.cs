namespace EirService.HelpRequests.Domain.Entities;

public sealed class EirRequest
{
    private EirRequest()
    {
    }

    public EirRequest(string id, string userId, string message)
    {
        Id = id;
        UserId = userId;
        Message = message;
    }

    public string Id { get; private set; } = null!;

    // Legacy rows have no recoverable owner; new requests always have a user id.
    public string? UserId { get; private set; }

    public string Message { get; private set; } = null!;
}
