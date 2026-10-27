using EirService.Contracts.Exceptions;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using FastEndpoints;
using Shared.Application.Persistence;

namespace EirService.HelpRequests.Application.Features.Student.CreateHelpRequest;

public sealed class CreateHelpRequestCommandHandler(
    IHelpRequestRepository repository, HelpRequestAccess access,
    IUnitOfWork<IHelpRequestRepository> unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<CreateHelpRequestCommand, HelpRequestResult>
{
    public async Task<HelpRequestResult> ExecuteAsync(CreateHelpRequestCommand command, CancellationToken cancellationToken)
    {
        var session = await access.ForSessionAsync(command.SessionId, command.StudentId, AuthorRole.Student, cancellationToken);
        
        if (await repository.HasOpenAsync(session.Id, session.Generation, command.StudentId, cancellationToken))
            throw new OperationConflictException("You already have an open request in this session.");
        
        var request = HelpRequest.Create(session.Id, session.Generation, command.StudentId,
            command.HelpType!.Value, new MessageText(command.Message), timeProvider.GetUtcNow());
        
        await repository.AddAsync(request, cancellationToken);
        
        await unitOfWork.SaveChangesAsync(cancellationToken);
        
        return await request.ToResultAsync(cancellationToken);
    }
}
