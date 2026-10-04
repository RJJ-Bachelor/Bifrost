using EirService.Requests.Application.Messaging;
using EirService.Requests.Application.Repositories;
using EirService.Requests.Domain.Entities;
using MediatR;

namespace EirService.Requests.Application.Features.Commands.CreateRequest
{
    public sealed class CreateRequestCommandHandler(
        IRequestRepository requestRepository,
        IRequestMessagePublisher messagePublisher)
        : IRequestHandler<CreateRequestCommand, string>
    {
        public async Task<string> Handle(CreateRequestCommand request, CancellationToken cancellationToken)
        {
            var eirRequest = new EirRequest(request.Id, request.Message);

            await requestRepository.AddAsync(eirRequest, cancellationToken);

            await messagePublisher.PublishRequestCreatedAsync(
                request.Id,
                request.Message,
                cancellationToken);

            return eirRequest.Id;
        }
    }
}
