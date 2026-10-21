using EirService.Contracts.KnowledgeBase.RegisterGeneralization;
using EirService.KnowledgeBase.Application.Repositories;
using EirService.KnowledgeBase.Domain.Entities;
using FastEndpoints;
using Shared.Application.Persistence;

namespace EirService.KnowledgeBase.Application.Features.Internal.RegisterGeneralization;

public sealed class RegisterGeneralizationCommandHandler(IFaqRepository repository,
    IUnitOfWork<IFaqRepository> unitOfWork, TimeProvider clock) : ICommandHandler<RegisterGeneralizationCommand, bool>
{
    public async Task<bool> ExecuteAsync(RegisterGeneralizationCommand command, CancellationToken cancellationToken)
    {
        var existing = await repository.GetAsync(command.Id, cancellationToken);
        if (existing is not null)
        {
            return existing.SessionId == command.SessionId && existing.Generation == command.Generation &&
                existing.Question == command.Question.Trim() && existing.HelpRequestIds.SequenceEqual(command.HelpRequestIds.Distinct().Order());
        }
        await repository.AddAsync(FaqAnswer.Create(command.Id, command.SessionId, command.Generation,
            command.Question, command.HelpRequestIds, clock.GetUtcNow()), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
