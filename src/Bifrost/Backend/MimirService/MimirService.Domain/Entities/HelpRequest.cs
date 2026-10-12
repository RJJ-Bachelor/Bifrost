using Shared.Domain.Primitives;
namespace MimirService.Domain.Entities;

public sealed class HelpRequest : AggregateRoot<string>
{
    private HelpRequest()
    {
    }

    private HelpRequest(string id, string message)
    {
        Id = id;
        Message = message;
        ReceivedAt = DateTimeOffset.UtcNow;
    }


    public string Message { get; private set; } = null!;

    public DateTimeOffset ReceivedAt { get; private set; }

    public static HelpRequest Create(string id, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new HelpRequest(id, message);
    }
}
