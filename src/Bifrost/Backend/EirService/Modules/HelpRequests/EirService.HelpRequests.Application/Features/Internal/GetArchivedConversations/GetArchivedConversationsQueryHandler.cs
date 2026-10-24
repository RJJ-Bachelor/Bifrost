using EirService.Contracts.HelpRequests.GetArchivedConversations;
using EirService.HelpRequests.Application.Repositories;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Internal.GetArchivedConversations;

public sealed class GetArchivedConversationsQueryHandler(IArchivedConversationRepository repository)
    : ICommandHandler<GetArchivedConversationsQuery, IReadOnlyList<ArchivedConversation>>
{
    public Task<IReadOnlyList<ArchivedConversation>> ExecuteAsync(GetArchivedConversationsQuery query, CancellationToken cancellationToken) =>
        repository.GetForGenerationAsync(query.SessionId, query.Generation, query.StudentId, cancellationToken);
}
