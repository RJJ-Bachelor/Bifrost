using EirService.KnowledgeBase.Application.Repositories;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Persistence;

namespace EirService.KnowledgeBase.Application.Features.Internal.FaqAnswered;

public sealed class FaqAnsweredMessageHandler(IFaqRepository repository, IUnitOfWork<IFaqRepository> unitOfWork)
{
    public async Task HandleAsync(FaqAnsweredMessage message, CancellationToken cancellationToken)
    {
        var faq = await repository.GetAsync(message.FaqId, cancellationToken)
            ?? throw new InvalidOperationException("The FAQ must be stored before its answer.");
        if (faq.SessionId != message.SessionId || faq.Generation != message.Generation)
            throw new InvalidOperationException("The answer does not belong to this FAQ generation.");
        faq.RecordAnswer(message.Answer, message.TeacherId, message.AnsweredAt);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
