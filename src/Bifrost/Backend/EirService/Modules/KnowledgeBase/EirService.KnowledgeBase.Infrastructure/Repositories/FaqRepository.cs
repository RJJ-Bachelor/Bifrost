using EirService.KnowledgeBase.Application.Repositories;
using EirService.KnowledgeBase.Domain.Entities;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EirService.KnowledgeBase.Infrastructure.Repositories;

public sealed class FaqRepository(EirKnowledgeBaseDbContext dbContext) : IFaqRepository
{
    public Task<FaqAnswer?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Faqs.SingleOrDefaultAsync(faq => faq.Id == id, cancellationToken);
    public async Task AddAsync(FaqAnswer faq, CancellationToken cancellationToken) =>
        await dbContext.Faqs.AddAsync(faq, cancellationToken);
    public async Task<IReadOnlyList<FaqAnswer>> GetForSessionAsync(Guid sessionId, int generation, CancellationToken cancellationToken) =>
        await dbContext.Faqs.AsNoTracking().Where(faq => faq.SessionId == sessionId && faq.Generation == generation)
            .OrderBy(faq => faq.CreatedAt).ThenBy(faq => faq.Id).ToArrayAsync(cancellationToken);
}
