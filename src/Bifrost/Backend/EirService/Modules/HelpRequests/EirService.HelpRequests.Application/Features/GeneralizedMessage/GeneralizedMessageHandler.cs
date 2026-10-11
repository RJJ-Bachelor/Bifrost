using EirService.Contracts.Sessions.GetSessionGenerations;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace EirService.HelpRequests.Application.Features.GeneralizedMessage;

public sealed class GeneralizedMessageHandler(ILogger<GeneralizedMessageHandler> logger) : IGeneralizedMessageHandler
{
    public async Task HandleAsync(MessageEnvelope<GeneralizedCreatedMessage> message, CancellationToken cancellationToken)
    {
        var payload = message.Payload;
        if (payload.SessionId is { } sessionId)
        {
            var generations = await new GetSessionGenerationsQuery([sessionId]).ExecuteAsync(cancellationToken);
            if (!generations.TryGetValue(sessionId, out var current) || payload.Generation != current)
            {
                logger.LogInformation("Discarded generalized message {MessageId} from an obsolete session generation.", message.Id);
                return;
            }
        }
        else if (payload.Generation is not null) return;

        // There is no answer/FAQ persistence flow yet. Future writes must keep payload.Generation.
        logger.LogInformation("Eir received generalized message {MessageId} with payload: {Payload}", message.Id, payload.Message);
    }
}
