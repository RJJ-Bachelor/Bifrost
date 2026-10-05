using Microsoft.Extensions.Logging;
using MimirService.Application.Repositories;
using MimirService.Domain.Entities;
using Shared.Application.Messaging;

namespace MimirService.Application.Services.Messaging;

public sealed class RequestMessageHandler(
    ILogger<RequestMessageHandler> logger,
    IGeneralizedMessagePublisher messagePublisher,
    IMimirRequestRepository requestRepository) : IRequestMessageHandler
{
    public async Task HandleAsync(
        MessageEnvelope<RequestCreatedMessage> message,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Mimir received request message {MessageId} with payload: {Payload}",
            message.Id,
            message.Payload.Message);

        var request = MimirRequest.Create(
            message.Id,
            message.Payload.Message);

        await requestRepository.AddAsync(request, cancellationToken);

        await messagePublisher.PublishGeneralizedCreatedAsync(
            message.Id,
            message.Payload.Message,
            cancellationToken);
    }
}
