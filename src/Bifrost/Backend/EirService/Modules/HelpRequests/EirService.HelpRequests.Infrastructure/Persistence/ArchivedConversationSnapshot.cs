namespace EirService.HelpRequests.Infrastructure.Persistence;

// Read model retained when an archived generation's queued request is deleted.
public sealed class ArchivedConversationSnapshot
{
    public Guid Id { get; init; }
    public Guid SessionId { get; init; }
    public int Generation { get; init; }
    public string StudentId { get; init; } = null!;
    public string Data { get; init; } = null!;
}
