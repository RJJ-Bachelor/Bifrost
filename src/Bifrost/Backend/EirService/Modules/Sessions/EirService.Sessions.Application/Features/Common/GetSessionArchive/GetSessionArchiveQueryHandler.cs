using EirService.Contracts.Exceptions;
using EirService.Contracts.HelpRequests.GetArchivedConversations;
using EirService.Contracts.KnowledgeBase.GetArchivedFaqs;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.Common.GetSessionArchive;

public sealed class GetSessionArchiveQueryHandler(ISessionRepository repository) : ICommandHandler<GetSessionArchiveQuery, SessionArchiveResult>
{
    public async Task<SessionArchiveResult> ExecuteAsync(GetSessionArchiveQuery query, CancellationToken cancellationToken)
    {
        await SessionArchiveAccess.EnsureAsync(repository, query.SessionId, query.ActorId, query.IsTeacher, cancellationToken);
        var archive = await repository.GetArchiveAsync(query.SessionId, query.ArchiveId, cancellationToken)
            ?? throw new ResourceNotFoundException();
        var faqs = await new GetArchivedFaqsQuery(archive.SessionId, archive.Generation, query.IsTeacher)
            .ExecuteAsync(cancellationToken);
        var conversations = await new GetArchivedConversationsQuery(
            archive.SessionId, archive.Generation, query.IsTeacher ? null : query.ActorId).ExecuteAsync(cancellationToken);
        return new SessionArchiveResult(archive.Id, archive.SessionId, archive.Title, archive.Generation,
            archive.StartedAtUtc, archive.ArchivedAtUtc, faqs, conversations);
    }
}
