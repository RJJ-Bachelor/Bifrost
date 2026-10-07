using System.Security.Claims;
using EirService.Requests.Application.Repositories;

namespace EirService.Api.Endpoints.Student;

internal static class Identity
{
    public static void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/students/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            UserId = user.FindFirst("sub")!.Value,
            Roles = user.FindAll("role").Select(claim => claim.Value).ToArray()
        })).WithTags(Tags.Students).RequireAuthorization("Student");

        app.MapGet("/students/requests", async (
            ClaimsPrincipal user, IRequestRepository repository, CancellationToken cancellationToken) =>
            Results.Ok(await repository.GetByUserIdAsync(user.FindFirst("sub")!.Value, cancellationToken)))
            .WithTags(Tags.Students).RequireAuthorization("Student");
    }
}
