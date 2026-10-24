using FastEndpoints;

namespace EirService.Contracts.HelpRequests.GetArchivedConversations;

// Sessions authorizes the caller before requesting content from this module.
public sealed record GetArchivedConversationsQuery(Guid SessionId, int Generation, string? StudentId)
    : ICommand<IReadOnlyList<ArchivedConversation>>;

public sealed record ArchivedConversation(
    Guid Id, Guid SessionId, int Generation, int RequestNumber, string DisplayNumber,
    string StudentId, string HelpType, string Status, string? AssignedTeacherId,
    DateTimeOffset CreatedAt, DateTimeOffset QueuedAt, DateTimeOffset? ReturnedToQueueAt,
    DateTimeOffset? StartedAt, DateTimeOffset? ClosedAt, Guid? FaqAnswerId,
    IReadOnlyList<ArchivedConversationMessage> Messages);

public sealed record ArchivedConversationMessage(
    Guid Id, Guid HelpRequestId, string AuthorId, string AuthorRole, string MessageType,
    string Text, DateTimeOffset CreatedAt, int SequenceNumber);
