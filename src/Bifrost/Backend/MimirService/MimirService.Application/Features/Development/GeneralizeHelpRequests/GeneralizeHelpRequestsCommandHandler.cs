using FluentValidation;
using MimirService.Application.Services.Messaging;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Application.Features.Development.GeneralizeHelpRequests;

public sealed class GeneralizeHelpRequestsCommandHandler(
    IValidator<GeneralizeHelpRequestsCommand> validator, IGeneralizedMessagePublisher publisher)
{
    public const string Question = "Dette er et generaliseret spørgsmål.";

    public async Task<GeneralizeHelpRequestsResult> ExecuteAsync(
        GeneralizeHelpRequestsCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        
        var id = command.GeneralizationId ?? Guid.NewGuid();
        var ids = command.HelpRequestIds!.Order().ToArray();

        await publisher.PublishAsync(new GeneralizedCreatedMessage(id, command.SessionId,
            command.Generation, Question, ids), cancellationToken);
        
        return new GeneralizeHelpRequestsResult(id, command.SessionId, command.Generation, Question, ids);
    }
}
