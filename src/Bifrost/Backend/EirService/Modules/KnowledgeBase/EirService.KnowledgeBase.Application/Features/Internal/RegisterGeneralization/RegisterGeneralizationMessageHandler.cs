using EirService.Contracts.HelpRequests.ApplyGeneralization;
using EirService.Contracts.HelpRequests.CanApplyGeneralization;
using EirService.Contracts.Sessions.GetSessionGenerations;
using EirService.KnowledgeBase.Application.Repositories;
using EirService.KnowledgeBase.Domain.Entities;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Persistence;

namespace EirService.KnowledgeBase.Application.Features.Internal.RegisterGeneralization;

public sealed class RegisterGeneralizationMessageHandler(
    ILogger<RegisterGeneralizationMessageHandler> logger,
    IFaqRepository repository,
    IUnitOfWork<IFaqRepository> unitOfWork,
    TimeProvider clock,
    ICommandHandler<GetSessionGenerationsQuery, IReadOnlyDictionary<Guid, int>> sessions,
    ICommandHandler<CanApplyGeneralizationQuery, bool> sourceRequests,
    ICommandHandler<ApplyGeneralizationCommand, bool> helpRequests)
{
    public async Task HandleAsync(MessageEnvelope<GeneralizedCreatedMessage> message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (message.Payload is not { } payload)
        {
            logger.LogWarning("Discarded generalized message {MessageId} without a payload.", message.Id);
            return;
        }
        var ids = payload.HelpRequestIds?.Distinct().Order().ToArray() ?? [];
        if (payload.GeneralizationId == Guid.Empty || payload.SessionId == Guid.Empty || payload.Generation < 1 ||
            ids.Length < FaqAnswer.MinSourceCount || ids.Length > FaqAnswer.MaxSourceCount ||
            ids.Contains(Guid.Empty) || ids.Contains(payload.GeneralizationId) ||
            string.IsNullOrWhiteSpace(payload.Question) || payload.Question.Trim().Length > FaqAnswer.MaxTextLength)
        {
            logger.LogWarning("Discarded malformed generalized message {MessageId}.", message.Id);
            return;
        }

        var generations = await sessions.ExecuteAsync(new GetSessionGenerationsQuery([payload.SessionId]), cancellationToken);
        if (!generations.TryGetValue(payload.SessionId, out var current) || payload.Generation != current)
        {
            logger.LogInformation("Discarded generalized message {MessageId} from an obsolete session generation.", message.Id);
            return;
        }

        var question = payload.Question.Trim();
        var existing = await repository.GetAsync(payload.GeneralizationId, cancellationToken);
        if (existing is not null && (existing.SessionId != payload.SessionId || existing.Generation != current ||
            existing.Question != question || !existing.HelpRequestIds.SequenceEqual(ids)))
        {
            logger.LogWarning("Discarded generalized message {MessageId} with a conflicting generalization id.", message.Id);
            return;
        }

        var canApply = await sourceRequests.ExecuteAsync(new CanApplyGeneralizationQuery(
            payload.GeneralizationId, payload.SessionId, current, ids), cancellationToken);
        if (!canApply)
        {
            logger.LogWarning("Discarded generalized message {MessageId} with invalid source requests or an already used id.", message.Id);
            return;
        }

        if (existing is null)
        {
            await repository.AddAsync(FaqAnswer.Create(payload.GeneralizationId, payload.SessionId, current,
                question, ids, clock.GetUtcNow()), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // A failed HelpRequests commit leaves this delivery unacknowledged. Redelivery resumes here.
        var applied = await helpRequests.ExecuteAsync(new ApplyGeneralizationCommand(
            payload.GeneralizationId, payload.SessionId, current, question, ids), cancellationToken);
        if (!applied)
            logger.LogInformation("Generalization {GeneralizationId} could no longer be applied to the current help requests.", payload.GeneralizationId);
    }
}
