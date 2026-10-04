using MediatR;

namespace EirService.Requests.Application.Features.Commands.CreateRequest
{
    public class CreateRequestCommandHandler() : IRequestHandler<CreateRequestCommand, string>
    {
        public async Task<string> Handle(CreateRequestCommand request, CancellationToken cancellationToken)
        {
            // Comming soon - Implementation for handling the CreateRequestCommand

            return "Return form handler";
        }
    }
}
