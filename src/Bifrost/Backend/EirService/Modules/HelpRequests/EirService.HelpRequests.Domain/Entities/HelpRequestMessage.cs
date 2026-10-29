using EirService.HelpRequests.Domain.ValueObjects;
using Shared.Domain.Primitives;

namespace EirService.HelpRequests.Domain.Entities;

public sealed class HelpRequestMessage : Entity<Guid>
{
    private HelpRequestMessage() { }

    internal HelpRequestMessage(Guid helpRequestId, string authorId, AuthorRole authorRole, HelpRequestMessageType messageType,
        MessageText text, DateTimeOffset now, int sequenceNumber)
    {
        ArgumentNullException.ThrowIfNull(text);
        Id = Guid.NewGuid();
        HelpRequestId = helpRequestId;
        AuthorId = authorId;
        AuthorRole = authorRole;
        MessageType = messageType;
        Text = text;
        CreatedAt = now.ToUniversalTime();
        SequenceNumber = sequenceNumber;
    }

    public Guid HelpRequestId { get; private set; }
    public string AuthorId { get; private set; } = null!;
    public AuthorRole AuthorRole { get; private set; }
    public HelpRequestMessageType MessageType { get; private set; }
    public MessageText Text { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public int SequenceNumber { get; private set; }
    public bool IsRejected { get; private set; }

    internal void ReviseFaqQuestion(MessageText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (MessageType != HelpRequestMessageType.FaqQuestion || IsRejected)
            throw new HelpRequestRuleException("Only current FAQ questions can be edited.");
        Text = text;
    }

    internal void RejectFaq()
    {
        if (MessageType is not (HelpRequestMessageType.FaqQuestion or HelpRequestMessageType.FaqAnswer))
            throw new HelpRequestRuleException("Only FAQ messages can be rejected.");
        IsRejected = true;
    }
}
