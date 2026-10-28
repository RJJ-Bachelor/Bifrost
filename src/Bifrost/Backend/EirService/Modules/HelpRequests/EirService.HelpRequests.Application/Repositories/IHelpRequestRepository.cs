using EirService.Contracts.HelpRequests.GetWaitingCounts;
using EirService.HelpRequests.Domain.Entities;

namespace EirService.HelpRequests.Application.Repositories;

public interface IHelpRequestRepository
{
    Task AddAsync(HelpRequest request, CancellationToken cancellationToken);
    Task<HelpRequest?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<HelpRequest>> GetByUserIdAsync(string studentId, CancellationToken cancellationToken);
    // Returns requests in queue order: latest return first, then QueuedAt and Id.
    Task<IReadOnlyList<HelpRequest>> GetForSessionAsync(Guid sessionId, int generation, CancellationToken cancellationToken);
    // Returns scalar activity in the same queue order, without loading messages.
    Task<IReadOnlyList<HelpRequestActivity>> GetSessionActivityAsync(Guid sessionId, int generation, CancellationToken cancellationToken);
    Task<string[]> GetWaitingStudentIdsAsync(Guid sessionId, int generation, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, WaitingCounts>> GetWaitingCountsAsync(
        IReadOnlyDictionary<Guid, int> sessionGenerations, CancellationToken cancellationToken);
    Task<IReadOnlyList<HelpRequest>> GetByIdsAsync(Guid[] ids, CancellationToken cancellationToken);
    Task<IReadOnlyList<HelpRequest>> GetLinkedToFaqAsync(Guid faqId, CancellationToken cancellationToken);
    Task<bool> HasOpenAsync(Guid sessionId, int generation, string studentId, CancellationToken cancellationToken);
    Task<HelpRequest?> TakeNextAsync(Guid sessionId, int generation, string teacherId, CancellationToken cancellationToken);
}
