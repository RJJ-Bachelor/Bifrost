using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Application.Services.Messaging;

public sealed class FaqQuestionChangedMessageHandler(ILogger<FaqQuestionChangedMessageHandler> logger) : IFaqQuestionChangedMessageHandler
{
    public Task HandleAsync(MessageEnvelope<FaqQuestionChangedIntegrationEvent> message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var change = message.Payload;
        logger.LogInformation("Mimir received FAQ question change for FAQ {FaqId} in session {SessionId} at generation {Generation}, message {MessageId} at {ChangedAtUtc}",
            change.FaqId, change.SessionId, change.Generation, message.Id, change.ChangedAtUtc);
        return Task.CompletedTask;
    }
}
