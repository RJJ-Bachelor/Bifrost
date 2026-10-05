using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;

namespace MimirService.Application.Messaging;

public sealed class RequestMessageHandler(
    ILogger<RequestMessageHandler> logger,
    IGeneralizedMessagePublisher messagePublisher) : IRequestMessageHandler
{
    public async Task HandleAsync(
        MessageEnvelope<RequestCreatedMessage> message,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Mimir received request message {MessageId} with payload: {Payload}",
            message.Id,
            message.Payload.Message);

        await messagePublisher.PublishGeneralizedCreatedAsync(
            message.Id,
            message.Payload.Message,
            cancellationToken);
    }
}
