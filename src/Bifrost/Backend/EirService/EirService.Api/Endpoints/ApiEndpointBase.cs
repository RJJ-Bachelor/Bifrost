using MediatR;

namespace EirService.Api.Endpoints;

public abstract class ApiEndpointBase(ISender mediator)
{
    protected ISender Mediator { get; } = mediator;
}
