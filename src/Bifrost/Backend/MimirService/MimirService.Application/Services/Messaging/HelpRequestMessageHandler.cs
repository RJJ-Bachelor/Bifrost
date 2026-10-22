using Microsoft.Extensions.Logging;
using MimirService.Application.Repositories;
using MimirService.Domain.Entities;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Application.Services.Messaging;

public sealed class HelpRequestMessageHandler(ILogger<HelpRequestMessageHandler> logger,
    IHelpRequestRepository requestRepository) : IHelpRequestMessageHandler
{
    public async Task HandleAsync(MessageEnvelope<HelpRequestCreatedMessage> message, CancellationToken cancellationToken)
    {
        var payload = message.Payload;
        if (await requestRepository.ExistsAsync(payload.HelpRequestId, cancellationToken)) return;
        var request = HelpRequest.Create(payload.HelpRequestId, payload.SessionId, payload.Generation, payload.Message);
        
        if (await requestRepository.TryAddAsync(request, null, cancellationToken))
            logger.LogInformation("Mimir stored help request {HelpRequestId} for session {SessionId} generation {Generation}.",
                payload.HelpRequestId, payload.SessionId, payload.Generation);
    }
}
