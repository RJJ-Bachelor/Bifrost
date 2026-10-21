using EirService.Contracts.KnowledgeBase.RegisterGeneralization;
using EirService.Contracts.Sessions.GetSessionGenerations;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Persistence;

namespace EirService.HelpRequests.Application.Features.Internal.GeneralizedMessage;

public sealed class GeneralizedMessageHandler(ILogger<GeneralizedMessageHandler> logger,
    IHelpRequestRepository repository, IUnitOfWork<IHelpRequestRepository> unitOfWork, TimeProvider clock,
    ICommandHandler<RegisterGeneralizationCommand, bool> knowledgeBase) : IGeneralizedMessageHandler
{
    public async Task HandleAsync(MessageEnvelope<GeneralizedCreatedMessage> message, CancellationToken cancellationToken)
    {
        var payload = message.Payload;
        var ids = payload.HelpRequestIds?.Distinct().ToArray() ?? [];
        if (payload.GeneralizationId == Guid.Empty || ids.Length < 2 || ids.Length > 100 || ids.Contains(Guid.Empty) ||
            ids.Contains(payload.GeneralizationId) || string.IsNullOrWhiteSpace(payload.Question) || payload.Question.Trim().Length > MessageText.MaxLength)
        {
            logger.LogWarning("Discarded malformed generalized message {MessageId}.", message.Id);
            return;
        }
        var generations = await new GetSessionGenerationsQuery([payload.SessionId]).ExecuteAsync(cancellationToken);
        if (!generations.TryGetValue(payload.SessionId, out var current) || payload.Generation != current)
        {
            logger.LogInformation("Discarded generalized message {MessageId} from an obsolete session generation.", message.Id);
            return;
        }
        var sources = await repository.GetByIdsAsync(ids, cancellationToken);
        if (sources.Count != ids.Length || sources.Any(request => request.IsFaq || request.SessionId != payload.SessionId || request.Generation != current))
        {
            logger.LogWarning("Discarded generalized message {MessageId} with invalid source requests.", message.Id);
            return;
        }

        var existing = await repository.GetAsync(payload.GeneralizationId, cancellationToken);
        if (existing is not null && (!existing.IsFaq || existing.SessionId != payload.SessionId || existing.Generation != current))
        {
            logger.LogWarning("Discarded generalized message {MessageId} with an already used id.", message.Id);
            return;
        }
        // Resolve the module contract in the consumer's scope, including outside HTTP requests.
        var stored = await knowledgeBase.ExecuteAsync(new RegisterGeneralizationCommand(payload.GeneralizationId,
            payload.SessionId, current, payload.Question, ids), cancellationToken);
        if (!stored)
        {
            logger.LogWarning("Discarded generalized message {MessageId} with a conflicting generalization id.", message.Id);
            return;
        }
        if (existing is not null) return;
        var now = clock.GetUtcNow();
        foreach (var request in sources.Where(request => request.CanLinkToFaq))
            request.LinkToFaq(payload.GeneralizationId, now);
        await repository.AddAsync(HelpRequest.CreateFaq(payload.GeneralizationId, payload.SessionId, current,
            new MessageText(payload.Question), now), cancellationToken);
        // xmin protects every source against a simultaneous reservation, message or cancellation.
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
