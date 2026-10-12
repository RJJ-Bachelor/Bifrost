using Shared.Domain.Primitives;
namespace MimirService.Domain.Entities;

public sealed class HelpRequest : AggregateRoot<Guid>
{
    private HelpRequest()
    {
    }

    private HelpRequest(Guid id, string message)
    {
        Id = id;
        Message = message;
        ReceivedAt = DateTimeOffset.UtcNow;
    }


    public string Message { get; private set; } = null!;

    public DateTimeOffset ReceivedAt { get; private set; }

    public static HelpRequest Create(Guid id, string message)
    {
        if (id == Guid.Empty) throw new ArgumentException("An id is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new HelpRequest(id, message);
    }
}
