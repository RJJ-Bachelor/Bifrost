using EirService.Contracts.Exceptions;
using EirService.Contracts.Sessions.GetHelpRequestSession;
using EirService.KnowledgeBase.Application.Features.Common;
using EirService.KnowledgeBase.Application.Repositories;
using EirService.KnowledgeBase.Domain.Entities;
using FastEndpoints;
using Shared.Application.Persistence;

namespace EirService.KnowledgeBase.Application.Features.Teacher.CreateFaq;

public sealed class CreateFaqCommandHandler(
    IFaqRepository repository, IUnitOfWork<IFaqRepository> unitOfWork, TimeProvider clock)
    : ICommandHandler<CreateFaqCommand, FaqResult>
{
    public async Task<FaqResult> ExecuteAsync(CreateFaqCommand command, CancellationToken cancellationToken)
    {
        var session = await new GetHelpRequestSessionQuery(command.SessionId).ExecuteAsync(cancellationToken)
            ?? throw new ResourceNotFoundException();
        if (!session.TeacherIds.Contains(command.TeacherId, StringComparer.Ordinal))
            throw new ResourceNotFoundException();

        var faq = FaqAnswer.CreateAnswered(session.Id, session.Generation, command.Question,
            command.Answer, command.TeacherId, clock.GetUtcNow());
        await repository.AddAsync(faq, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return faq.ToResult();
    }
}
