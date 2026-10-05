using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;

namespace EirService.Requests.Application.Services.Messaging;

public sealed class GeneralizedMessageHandler(
    ILogger<GeneralizedMessageHandler> logger) : IGeneralizedMessageHandler
{
    public Task HandleAsync(
        MessageEnvelope<GeneralizedCreatedMessage> message,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Eir received generalized message {MessageId} with payload: {Payload}",
            message.Id,
            message.Payload.Message);

        return Task.CompletedTask;
    }
}
