using MediatR;

namespace EirService.Requests.Application.Features.Commands.CreateRequest
{
    public record CreateRequestCommand(
    string Id,
    string Message)
        : IRequest<string>;
}
