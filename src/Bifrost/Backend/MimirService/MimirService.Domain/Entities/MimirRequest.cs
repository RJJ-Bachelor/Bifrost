namespace MimirService.Domain.Entities;

public sealed class MimirRequest
{
    private MimirRequest()
    {
    }

    private MimirRequest(string id, string message)
    {
        Id = id;
        Message = message;
        ReceivedAt = DateTimeOffset.UtcNow;
    }

    public string Id { get; private set; } = null!;

    public string Message { get; private set; } = null!;

    public DateTimeOffset ReceivedAt { get; private set; }

    public static MimirRequest Create(string id, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new MimirRequest(id, message);
    }
}
