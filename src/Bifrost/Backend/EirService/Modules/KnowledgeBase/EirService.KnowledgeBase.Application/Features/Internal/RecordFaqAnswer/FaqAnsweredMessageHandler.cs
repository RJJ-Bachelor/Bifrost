using EirService.Contracts.KnowledgeBase.RecordFaqAnswer;
using FastEndpoints;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace EirService.KnowledgeBase.Application.Features.Internal.RecordFaqAnswer;

public sealed class FaqAnsweredMessageHandler(ICommandHandler<RecordFaqAnswerCommand> commands)
    : IMessageHandler<FaqAnsweredIntegrationEvent>
{
    public Task HandleAsync(MessageEnvelope<FaqAnsweredIntegrationEvent> message, CancellationToken cancellationToken)
    {
        var answer = message.Payload;
        if (answer.FaqId == Guid.Empty || answer.SessionId == Guid.Empty || answer.Generation < 1 ||
            string.IsNullOrWhiteSpace(answer.Answer) || answer.Answer.Length > 4000 ||
            string.IsNullOrWhiteSpace(answer.TeacherId) || answer.TeacherId.Length > 100 ||
            answer.Question is { Length: > 4000 })
            throw new MessageRejectedException("The FAQ answer has invalid identifiers, generation or text.");
        return commands.ExecuteAsync(new(answer.FaqId, answer.SessionId, answer.Generation, answer.Answer,
            answer.TeacherId, answer.AnsweredAt, answer.Question), cancellationToken);
    }
}
