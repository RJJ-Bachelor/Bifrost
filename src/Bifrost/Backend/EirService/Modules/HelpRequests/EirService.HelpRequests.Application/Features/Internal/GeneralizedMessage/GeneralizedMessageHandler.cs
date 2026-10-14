using EirService.Contracts.Sessions.GetSessionGenerations;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace EirService.HelpRequests.Application.Features.Internal.GeneralizedMessage;

public sealed class GeneralizedMessageHandler(ILogger<GeneralizedMessageHandler> logger) : IGeneralizedMessageHandler
{
    public async Task HandleAsync(MessageEnvelope<GeneralizedCreatedMessage> message, CancellationToken cancellationToken)
    {
        var payload = message.Payload;
        var generations = await new GetSessionGenerationsQuery([payload.SessionId]).ExecuteAsync(cancellationToken);
        if (!generations.TryGetValue(payload.SessionId, out var current) || payload.Generation != current)
        {
            logger.LogInformation("Discarded generalized message {MessageId} from an obsolete session generation.", message.Id);
            return;
        }

        // There is no answer/FAQ persistence flow yet. Future writes must keep payload.Generation.
        logger.LogInformation("Eir received generalized message {MessageId} with payload: {Payload}", message.Id, payload.Message);
    }
}
