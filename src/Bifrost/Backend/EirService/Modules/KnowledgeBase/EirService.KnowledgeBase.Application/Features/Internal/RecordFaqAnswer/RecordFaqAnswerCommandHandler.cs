using EirService.Contracts.KnowledgeBase.RecordFaqAnswer;
using EirService.KnowledgeBase.Application.Repositories;
using FastEndpoints;
using Shared.Application.Persistence;

namespace EirService.KnowledgeBase.Application.Features.Internal.RecordFaqAnswer;

public sealed class RecordFaqAnswerCommandHandler(IFaqRepository repository, IUnitOfWork<IFaqRepository> unitOfWork)
    : ICommandHandler<RecordFaqAnswerCommand>
{
    public async Task ExecuteAsync(RecordFaqAnswerCommand command, CancellationToken cancellationToken)
    {
        var faq = await repository.GetAsync(command.FaqId, cancellationToken)
            ?? throw new InvalidOperationException("The FAQ must be stored before its answer.");
        if (faq.SessionId != command.SessionId || faq.Generation != command.Generation)
            throw new InvalidOperationException("The answer does not belong to this FAQ generation.");
        faq.RecordAnswer(command.Answer, command.TeacherId, command.AnsweredAt, command.Question);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
