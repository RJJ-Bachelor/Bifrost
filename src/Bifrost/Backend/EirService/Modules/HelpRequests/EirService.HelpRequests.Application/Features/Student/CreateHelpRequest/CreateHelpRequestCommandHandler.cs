using EirService.Contracts.Participants.IsSessionParticipant;
using EirService.Contracts.Sessions.GetSessionGenerations;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;
using EirService.Contracts.Exceptions;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace EirService.HelpRequests.Application.Features.Student.CreateHelpRequest;

public sealed class CreateHelpRequestCommandHandler(
    IRequestRepository requestRepository)
    : ICommandHandler<CreateHelpRequestCommand, string>
{
    public async Task<string> ExecuteAsync(CreateHelpRequestCommand request, CancellationToken cancellationToken)
    {
        if (request.SessionId is { } sessionId)
        {
            if (!await new IsSessionParticipantQuery(sessionId, request.UserId).ExecuteAsync(cancellationToken))
                throw new ResourceNotFoundException();
            var generations = await new GetSessionGenerationsQuery([sessionId]).ExecuteAsync(cancellationToken);
            if (!generations.TryGetValue(sessionId, out var currentGeneration)) throw new ResourceNotFoundException();
            if (currentGeneration != request.Generation)
                throw new OperationConflictException("The session has been cleared. Refresh before submitting a request.");
        }

        // Keep the generation supplied and validated above, even if a clear races this write.
        var helpRequest = new HelpRequest(request.Id, request.UserId, request.Message, request.SessionId, request.Generation);
        OutgoingMessage[] messages =
        [
            new(request.Id, new HelpRequestCreatedMessage(request.Message, request.SessionId, request.Generation),
                MessageRoutes.Requests, MessageRoutes.HelpRequestCreated),
            new(request.Id, new NotificationCreatedMessage(request.UserId, request.Message, request.SessionId, request.Generation),
                MessageRoutes.Notifications, MessageRoutes.NotificationCreated)
        ];
        await requestRepository.AddAsync(helpRequest, messages, cancellationToken);
        return helpRequest.Id;
    }
}
