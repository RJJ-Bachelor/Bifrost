using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using MediatR;
using Shared.Application.Messaging;

namespace EirService.HelpRequests.Application.Features.Commands.CreateRequest
{
    public sealed class CreateRequestCommandHandler(
        IRequestRepository requestRepository,
        IRequestMessagePublisher messagePublisher,
        INotificationMessagePublisher notificationMessagePublisher)
        : IRequestHandler<CreateRequestCommand, string>
    {
        public async Task<string> Handle(CreateRequestCommand request, CancellationToken cancellationToken)
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
