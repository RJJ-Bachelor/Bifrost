using MediatR;

namespace EirService.HelpRequests.Application.Features.Commands.CreateRequest
{
    public record CreateRequestCommand(
    string Id,
    string UserId,
    string Message)
        : IRequest<string>;
}
