using EirService.HelpRequests.Domain.ValueObjects;
using Shared.Domain.Primitives;

namespace EirService.HelpRequests.Domain.Entities;

public sealed class HelpRequestMessage : Entity<Guid>
{
    private HelpRequestMessage() { }

    internal HelpRequestMessage(Guid helpRequestId, string authorId, AuthorRole authorRole,
        MessageText text, DateTimeOffset now, int sequenceNumber)
    {
        ArgumentNullException.ThrowIfNull(text);
        Id = Guid.NewGuid();
        HelpRequestId = helpRequestId;
        AuthorId = authorId;
        AuthorRole = authorRole;
        Text = text;
        CreatedAt = now.ToUniversalTime();
        SequenceNumber = sequenceNumber;
    }

    public Guid HelpRequestId { get; private set; }
    public string AuthorId { get; private set; } = null!;
    public AuthorRole AuthorRole { get; private set; }
    public MessageText Text { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public int SequenceNumber { get; private set; }
}
