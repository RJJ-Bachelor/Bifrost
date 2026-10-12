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
        var llmResponse = await llmClient.GenerateAsync(SystemInstruction, message.Payload.Message, cancellationToken);
        logger.LogInformation("Mimir generated LLM response for message {MessageId}: {Response}", message.Id, llmResponse);

        var request = HelpRequest.Create(message.Id, message.Payload.Message);
        var reply = new OutgoingMessage(message.Id,
            new GeneralizedCreatedMessage(llmResponse, message.Payload.SessionId, message.Payload.Generation),
            MessageRoutes.Requests, MessageRoutes.GeneralizedCreated);
        if (await requestRepository.TryAddAsync(request, reply, cancellationToken))
            logger.LogInformation("Mimir received help request {MessageId} with payload: {Payload}", message.Id, message.Payload.Message);
    }
}
