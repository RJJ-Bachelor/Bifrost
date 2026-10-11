using Microsoft.Extensions.Logging;
using MimirService.Application.Repositories;
using MimirService.Domain.Entities;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace MimirService.Application.Services.Messaging;

public sealed class HelpRequestMessageHandler(
    ILogger<HelpRequestMessageHandler> logger,
    IMimirRequestRepository requestRepository) : IHelpRequestMessageHandler
{
    public async Task HandleAsync(MessageEnvelope<HelpRequestCreatedMessage> message, CancellationToken cancellationToken)
    {
        var request = MimirRequest.Create(message.Id, message.Payload.Message);
        var reply = new OutgoingMessage(message.Id,
            new GeneralizedCreatedMessage(message.Payload.Message, message.Payload.SessionId, message.Payload.Generation),
            MessageRoutes.Requests, MessageRoutes.GeneralizedCreated);
        if (await requestRepository.TryAddAsync(request, reply, cancellationToken))
            logger.LogInformation("Mimir received help request {MessageId} with payload: {Payload}", message.Id, message.Payload.Message);
    }
}
