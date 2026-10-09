using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;
using Shared.Application.Messaging;

namespace EirService.HelpRequests.Application.Features.Commands.CreateHelpRequest
{
    public sealed class CreateHelpRequestCommandHandler(
        IRequestRepository requestRepository,
        IRequestMessagePublisher messagePublisher,
        INotificationMessagePublisher notificationMessagePublisher)
        : ICommandHandler<CreateHelpRequestCommand, string>
    {
        public async Task<string> ExecuteAsync(CreateHelpRequestCommand request, CancellationToken cancellationToken)
        {
            var eirRequest = new EirRequest(request.Id, request.UserId, request.Message);

            await requestRepository.AddAsync(eirRequest, cancellationToken);

            await messagePublisher.PublishRequestCreatedAsync(
                request.Id,
                request.Message,
                cancellationToken);

            await notificationMessagePublisher.PublishNotificationCreatedAsync(
                request.Id,
                request.UserId,
                request.Message,
                cancellationToken);

            return eirRequest.Id;
        }
    }
}
