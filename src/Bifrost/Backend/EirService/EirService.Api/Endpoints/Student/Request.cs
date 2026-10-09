using EirService.Api.Endpoints;
using EirService.HelpRequests.Application.Features.Commands.CreateRequest;
using MediatR;

namespace EirService.Api.Endpoints.Student
{
    internal sealed class Request(ISender mediator) : ApiEndpointBase(mediator)
    {
        public sealed record CreateRequestBody(string Id, string Message);

        public static void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost(
                    "/students/createrequest",
                    async (
                        CreateRequestBody request,
                        HttpContext context,
                        Request endpoint,
                        CancellationToken cancellationToken) =>
                        await endpoint.CreateRequest(request, context, cancellationToken))
                .WithTags(Tags.Students)
                .RequireAuthorization("Student");
        }

        private async Task<IResult> CreateRequest(
            CreateRequestBody request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Id) || request.Id.Length > 100 ||
                string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 4000)
            {
                return Results.BadRequest(new { error = "Id (1-100 characters) and Message (1-4000 characters) are required." });
            }

            var userId = context.User.FindFirst("sub")!.Value;
            var result = await Mediator.Send(
                new CreateRequestCommand(request.Id, userId, request.Message), cancellationToken);

            return Results.Ok(result);
        }
    }
}
