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
        var generalizations = await requestRepository.GetGeneralizationsAsync(payload.SessionId, payload.Generation, cancellationToken);
        var decision = await decider.DecideAsync(payload.Message, Candidates(generalizations, waiting), cancellationToken);
        logger.LogInformation("Mimir decided {Decision} for help request {HelpRequestId} with candidate {CandidateId}.",
            decision.Type, payload.HelpRequestId, decision.CandidateId);

        var (generalization, reply) = decision.Type switch
        {
            GeneralizationDecisionType.Pair => Pair(request, decision, waiting),
            _ => (null, null)
        };

        if (await requestRepository.TryAddAsync(request, generalization, reply, cancellationToken))
            logger.LogInformation("Mimir stored help request {HelpRequestId} for session {SessionId} generation {Generation}.",
                payload.HelpRequestId, payload.SessionId, payload.Generation);
    }

    private static GeneralizationCandidate[] Candidates(IEnumerable<Generalization> generalizations,
        IEnumerable<HelpRequest> waiting) =>
    [
        .. generalizations.Select(generalization =>
            new GeneralizationCandidate(generalization.Id, GeneralizationCandidateType.Generalization, generalization.Question)),
        .. waiting.Select(request =>
            new GeneralizationCandidate(request.Id, GeneralizationCandidateType.Request, request.Message))
    ];

    private (Generalization? Generalization, OutgoingMessage? Reply) Pair(HelpRequest request,
        GeneralizationDecision decision, IReadOnlyList<HelpRequest> waiting)
    {
        var partner = waiting.FirstOrDefault(candidate => candidate.Id == decision.CandidateId);
        if (partner is null || decision.FaqQuestion is null) return (null, null);

        var generalization = Generalization.Create(Guid.NewGuid(), request.SessionId, request.Generation,
            decision.FaqQuestion, timeProvider.GetUtcNow());
        partner.LinkToGeneralization(generalization);
        request.LinkToGeneralization(generalization);
        var reply = new OutgoingMessage(generalization.Id,
            new GeneralizedCreatedMessage(generalization.Id, generalization.SessionId, generalization.Generation,
                generalization.Question, new[] { partner.Id, request.Id }.Order().ToArray()),
            MessageRoutes.Requests, MessageRoutes.GeneralizedCreated);
        return (generalization, reply);
    }
}
