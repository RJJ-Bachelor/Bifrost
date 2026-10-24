using EirService.Contracts.KnowledgeBase.GetArchivedFaqs;
using EirService.KnowledgeBase.Application.Repositories;
using FastEndpoints;

namespace EirService.KnowledgeBase.Application.Features.Internal.GetArchivedFaqs;

public sealed class GetArchivedFaqsQueryHandler(IFaqRepository repository)
    : ICommandHandler<GetArchivedFaqsQuery, IReadOnlyList<ArchivedFaq>>
{
    public async Task<IReadOnlyList<ArchivedFaq>> ExecuteAsync(GetArchivedFaqsQuery query, CancellationToken cancellationToken) =>
        (await repository.GetForSessionAsync(query.SessionId, query.Generation, cancellationToken))
        .Where(faq => query.IncludeUnanswered || faq.Answer is not null)
        .Select(faq => new ArchivedFaq(faq.Id, faq.SessionId, faq.Generation, faq.Question, faq.Answer,
            faq.HelpRequestIds.Count, faq.CreatedAt, faq.AnsweredAt)).ToArray();
}
