using Microsoft.Extensions.Logging;
using MimirService.Application.Repositories;
using MimirService.Application.Services.Llm;
using MimirService.Domain.Entities;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace MimirService.Application.Services.Messaging;

public sealed class HelpRequestMessageHandler(
    ILogger<HelpRequestMessageHandler> logger,
    IHelpRequestRepository requestRepository,
    ILlmClient llmClient) : IHelpRequestMessageHandler
{
    // Placeholder prompt to verify the LLM connection
    private const string SystemInstruction = "Reply to the student's message in one short sentence.";

    public async Task HandleAsync(MessageEnvelope<HelpRequestCreatedMessage> message, CancellationToken cancellationToken)
    {
        // A redelivery must not cost another LLM call
        if (await requestRepository.ExistsAsync(message.Id, cancellationToken))
        {
            logger.LogInformation("Mimir ignored redelivered help request {MessageId}.", message.Id);
            return;
        }

        var payload = message.Payload;
        string llmResponse;
        try
        {
            llmResponse = await llmClient.GenerateAsync(SystemInstruction, payload.Message, cancellationToken);
            logger.LogInformation("Mimir generated LLM response for help request {MessageId}.", message.Id);
        }
        catch (LlmUnavailableException exception)
        {
            logger.LogWarning(exception, "Mimir forwarded help request {MessageId} unchanged because no LLM model responded.", message.Id);
            llmResponse = payload.Message;
        }

        var request = HelpRequest.Create(message.Id, payload.SessionId, payload.Generation, payload.Message);
        var reply = new OutgoingMessage(message.Id,
            new GeneralizedCreatedMessage(llmResponse, payload.SessionId, payload.Generation),
            MessageRoutes.Requests, MessageRoutes.GeneralizedCreated);
        if (await requestRepository.TryAddAsync(request, reply, cancellationToken))
            logger.LogInformation("Mimir stored help request {MessageId} for session {SessionId} generation {Generation}.",
                message.Id, payload.SessionId, payload.Generation);
    }
}
