using Microsoft.Extensions.Logging;
using MimirService.Application.Repositories;
using MimirService.Application.Services.Generalizations;
using MimirService.Domain.Entities;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace MimirService.Application.Services.Messaging;

public sealed class HelpRequestMessageHandler(ILogger<HelpRequestMessageHandler> logger,
    IHelpRequestRepository requestRepository, IGeneralizationDecider decider, TimeProvider timeProvider) : IHelpRequestMessageHandler
{
    public async Task HandleAsync(MessageEnvelope<HelpRequestCreatedMessage> message, CancellationToken cancellationToken)
    {
        var payload = message.Payload;
        if (await requestRepository.ExistsAsync(payload.HelpRequestId, cancellationToken)) return;
        var request = HelpRequest.Create(payload.HelpRequestId, payload.SessionId, payload.Generation, payload.Message);

        var waiting = await requestRepository.GetWaitingAsync(payload.SessionId, payload.Generation, cancellationToken);
        var candidates = waiting
            .Select(candidate => new GeneralizationCandidate(candidate.Id, GeneralizationCandidateType.Request, candidate.Message))
            .ToArray();
        var decision = await decider.DecideAsync(payload.Message, candidates, cancellationToken);
        logger.LogInformation("Mimir decided {Decision} for help request {HelpRequestId} with candidate {CandidateId}.",
            decision.Type, payload.HelpRequestId, decision.CandidateId);

        Generalization? generalization = null;
        OutgoingMessage? reply = null;
        if (decision is { Type: GeneralizationDecisionType.Pair, FaqQuestion: { } question } &&
            waiting.FirstOrDefault(candidate => candidate.Id == decision.CandidateId) is { } partner)
        {
            generalization = Generalization.Create(Guid.NewGuid(), payload.SessionId, payload.Generation,
                question, timeProvider.GetUtcNow());
            partner.LinkToGeneralization(generalization);
            request.LinkToGeneralization(generalization);
            reply = new OutgoingMessage(generalization.Id,
                new GeneralizedCreatedMessage(generalization.Id, payload.SessionId, payload.Generation,
                    generalization.Question, new[] { partner.Id, request.Id }.Order().ToArray()),
                MessageRoutes.Requests, MessageRoutes.GeneralizedCreated);
        }

        if (await requestRepository.TryAddAsync(request, generalization, reply, cancellationToken))
            logger.LogInformation("Mimir stored help request {HelpRequestId} for session {SessionId} generation {Generation}.",
                payload.HelpRequestId, payload.SessionId, payload.Generation);
    }
}
