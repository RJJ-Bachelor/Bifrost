using EirService.Contracts.HelpRequests.GetWaitingCounts;
using EirService.HelpRequests.Domain.Entities;

namespace EirService.HelpRequests.Application.Repositories;

public interface IHelpRequestRepository
{
    Task AddAsync(HelpRequest request, CancellationToken cancellationToken);
    Task<HelpRequest?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<HelpRequest>> GetByUserIdAsync(string studentId, Guid? sessionId = null,
        CancellationToken cancellationToken = default);
    // Returns scalar request activity without loading messages. Callers apply queue ordering when needed.
    Task<IReadOnlyList<HelpRequest>> GetForSessionAsync(Guid sessionId, int generation, CancellationToken cancellationToken);
    // Queue-start notifications need existence only, including reserved pending requests and FAQs.
    Task<bool> HasOtherPendingAsync(Guid sessionId, int generation, Guid excludedRequestId, CancellationToken cancellationToken);
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
