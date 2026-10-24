using EirService.Contracts.HelpRequests.GetArchivedConversations;
using EirService.Contracts.KnowledgeBase.GetArchivedFaqs;
using EirService.Sessions.Domain.Entities;

namespace EirService.Sessions.Application.Features.Common;

public sealed record SessionArchiveSummary(Guid Id, Guid SessionId, string Title, int Generation,
    DateTimeOffset StartedAtUtc, DateTimeOffset ArchivedAtUtc,
    int PrivateConversationCount, int GeneralizedQuestionCount)
{
    public static SessionArchiveSummary From(SessionArchive archive, int privateConversationCount, int generalizedQuestionCount) => new(
        archive.Id, archive.SessionId, archive.Title, archive.Generation, archive.StartedAtUtc, archive.ArchivedAtUtc,
        privateConversationCount, generalizedQuestionCount);
}

public sealed record SessionArchiveResult(Guid Id, Guid SessionId, string Title, int Generation,
    DateTimeOffset StartedAtUtc, DateTimeOffset ArchivedAtUtc,
    IReadOnlyList<ArchivedFaq> Faqs, IReadOnlyList<ArchivedConversation> Conversations);
