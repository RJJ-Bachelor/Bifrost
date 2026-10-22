using EirService.KnowledgeBase.Domain.Entities;

namespace EirService.KnowledgeBase.Application.Repositories;

public interface IFaqRepository
{
    Task<FaqAnswer?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(FaqAnswer faq, CancellationToken cancellationToken);
    Task<IReadOnlyList<FaqAnswer>> GetForSessionAsync(Guid sessionId, int generation, CancellationToken cancellationToken);
}
