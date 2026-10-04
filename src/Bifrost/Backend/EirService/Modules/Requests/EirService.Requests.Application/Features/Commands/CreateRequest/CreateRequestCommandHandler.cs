using EirService.Requests.Application.Repositories;
using EirService.Requests.Domain.Entities;
using MediatR;

namespace EirService.Requests.Application.Features.Commands.CreateRequest
{
    public sealed class CreateRequestCommandHandler(IRequestRepository requestRepository)
        : IRequestHandler<CreateRequestCommand, string>
    {
        public async Task<string> Handle(CreateRequestCommand request, CancellationToken cancellationToken)
        {
            var eirRequest = new EirRequest(request.Id, request.Message);

            await requestRepository.AddAsync(eirRequest, cancellationToken);

            return eirRequest.Id;
        }
    }
}
