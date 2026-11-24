using Microsoft.Extensions.Logging;
using MimirService.Application.Repositories;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Application.Services.Messaging;

public sealed class FaqQuestionChangedMessageHandler(ILogger<FaqQuestionChangedMessageHandler> logger,
    IHelpRequestRepository requestRepository) : IFaqQuestionChangedMessageHandler
{
    public async Task HandleAsync(MessageEnvelope<FaqQuestionChangedIntegrationEvent> message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var change = message.Payload;

        // Eir uses the generalization id Mimir sent as the FAQ id.
        var generalization = await requestRepository.GetGeneralizationAsync(change.FaqId, cancellationToken);
        if (generalization is null)
        {
            logger.LogInformation("Mimir ignored FAQ question change for unknown FAQ {FaqId} in session {SessionId}, message {MessageId}",
                change.FaqId, change.SessionId, message.Id);
            return;
        }

        if (generalization.SessionId != change.SessionId || generalization.Generation != change.Generation)
        {
            logger.LogWarning("Mimir ignored FAQ question change for FAQ {FaqId}: the message is for session {SessionId} at generation {Generation}, but the generalization belongs to session {StoredSessionId} at generation {StoredGeneration}, message {MessageId}",
                change.FaqId, change.SessionId, change.Generation, generalization.SessionId, generalization.Generation, message.Id);
            return;
        }

        try
        {
            generalization.ChangeQuestion(change.Question);
        }
        catch (ArgumentException)
        {
            // A throwing handler is requeued forever, and an invalid question stays invalid.
            logger.LogWarning("Mimir ignored FAQ question change for FAQ {FaqId} with an empty or too long question, message {MessageId}",
                change.FaqId, message.Id);
            return;
        }

        await requestRepository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Mimir changed the question of generalization {GeneralizationId} in session {SessionId} at generation {Generation}, message {MessageId} at {ChangedAtUtc}",
            generalization.Id, change.SessionId, change.Generation, message.Id, change.ChangedAtUtc);
    }
}
