using EirService.Contracts.HelpRequests.ApplyGeneralization;
using EirService.Contracts.Sessions.GetSessionGenerations;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using FastEndpoints;
using Shared.Application.Persistence;

namespace EirService.HelpRequests.Application.Features.Internal.ApplyGeneralization;

public sealed class ApplyGeneralizationCommandHandler(
    IHelpRequestRepository repository,
    IUnitOfWork<IHelpRequestRepository> unitOfWork,
    TimeProvider clock,
    ICommandHandler<GetSessionGenerationsQuery, IReadOnlyDictionary<Guid, int>> sessionGenerations)
    : ICommandHandler<ApplyGeneralizationCommand, bool>
{
    public async Task<bool> ExecuteAsync(ApplyGeneralizationCommand command, CancellationToken cancellationToken)
    {
        if (command.GeneralizationId == Guid.Empty || command.SessionId == Guid.Empty || command.Generation < 1 ||
            string.IsNullOrWhiteSpace(command.Question) || command.Question.Trim().Length > MessageText.MaxLength)
            return false;

        var generations = await sessionGenerations.ExecuteAsync(new GetSessionGenerationsQuery([command.SessionId]), cancellationToken);
        if (!generations.TryGetValue(command.SessionId, out var current) || current != command.Generation)
            return false;

        var targets = await GeneralizationTargets.FindAsync(repository, command.GeneralizationId, command.SessionId,
            command.Generation, command.HelpRequestIds, cancellationToken);
        if (targets is null) return false;

        var question = new MessageText(command.Question.Trim());
        if (targets.ExistingFaq is not null)
            return targets.ExistingFaq.Messages.SingleOrDefault(message => message.MessageType == HelpRequestMessageType.FaqQuestion)?.Text == question;

        var now = clock.GetUtcNow();
        foreach (var request in targets.Sources.Where(request => request.CanLinkToFaq))
            request.LinkToFaq(command.GeneralizationId, question, now);

        await repository.AddAsync(HelpRequest.CreateFaq(command.GeneralizationId, command.SessionId,
            command.Generation, question, now), cancellationToken);
        // Commit the FAQ, source conversations and their outbox events together. Source xmin
        // conflicts propagate so the consumer retries against the latest request state.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
