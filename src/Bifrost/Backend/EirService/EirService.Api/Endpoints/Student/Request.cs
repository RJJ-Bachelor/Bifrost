using EirService.Api.Endpoints;
using EirService.Requests.Application.Features.Commands.CreateRequest;
using MediatR;

namespace EirService.Api.Endpoints.Student
{
    internal sealed class Request(ISender mediator) : ApiEndpointBase(mediator)
    {
        public static void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost(
                    "/students/createrequest",
                    async (
                        CreateRequestCommand request,
                        Request endpoint,
                        CancellationToken cancellationToken) =>
                        await endpoint.CreateRequest(request, cancellationToken))
                .WithTags(Tags.Students);
        }

        private async Task<IResult> CreateRequest(
            CreateRequestCommand request,
            CancellationToken cancellationToken)
        {
            var result = await Mediator.Send(request, cancellationToken);

            return Results.Ok(result);
        }
    }
}