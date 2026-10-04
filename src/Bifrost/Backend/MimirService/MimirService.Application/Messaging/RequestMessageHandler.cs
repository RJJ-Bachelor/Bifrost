using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;

namespace MimirService.Application.Messaging;

public sealed class RequestMessageHandler(
    ILogger<RequestMessageHandler> logger) : IRequestMessageHandler
{
    public Task HandleAsync(
        MessageEnvelope<RequestCreatedMessage> message,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Mimir received request message {MessageId} with payload: {Payload}",
            message.Id,
            message.Payload.Message);

        return Task.CompletedTask;
    }
}
