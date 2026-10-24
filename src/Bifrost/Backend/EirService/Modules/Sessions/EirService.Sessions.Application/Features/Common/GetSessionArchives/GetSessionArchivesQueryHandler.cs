using EirService.Contracts.HelpRequests.GetArchivedConversationCounts;
using EirService.Contracts.KnowledgeBase.GetArchivedFaqCounts;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.Common.GetSessionArchives;

public sealed class GetSessionArchivesQueryHandler(ISessionRepository repository) : ICommandHandler<GetSessionArchivesQuery, IReadOnlyList<SessionArchiveSummary>>
{
    public async Task<IReadOnlyList<SessionArchiveSummary>> ExecuteAsync(GetSessionArchivesQuery query, CancellationToken cancellationToken)
    {
        await SessionArchiveAccess.EnsureAsync(repository, query.SessionId, query.ActorId, query.IsTeacher, cancellationToken);
        var archives = await repository.GetArchivesAsync(query.SessionId, cancellationToken);
        if (archives.Count == 0) return [];

        var generations = archives.Select(archive => archive.Generation).ToArray();
        var conversations = await new GetArchivedConversationCountsQuery(
            query.SessionId, generations, query.IsTeacher ? null : query.ActorId).ExecuteAsync(cancellationToken);
        var faqs = await new GetArchivedFaqCountsQuery(query.SessionId, generations, query.IsTeacher)
            .ExecuteAsync(cancellationToken);
        return archives.Select(archive => SessionArchiveSummary.From(archive,
            conversations.GetValueOrDefault(archive.Generation), faqs.GetValueOrDefault(archive.Generation))).ToArray();
    }
}
