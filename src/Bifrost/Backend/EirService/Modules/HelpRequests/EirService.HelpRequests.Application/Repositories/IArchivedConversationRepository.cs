using EirService.Contracts.HelpRequests.GetArchivedConversations;

namespace EirService.HelpRequests.Application.Repositories;

public interface IArchivedConversationRepository
{
    Task ArchiveQueueAsync(Guid sessionId, int generation, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<int, int>> CountForGenerationsAsync(
        Guid sessionId, int[] generations, string? studentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ArchivedConversation>> GetForGenerationAsync(
        Guid sessionId, int generation, string? studentId, CancellationToken cancellationToken);
}
