using System.Security.Claims;

namespace EirService.Api.Endpoints.Teacher;

internal static class Identity
{
    public static void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/teachers/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            UserId = user.FindFirst("sub")!.Value,
            Name = user.Identity!.Name,
            Roles = user.FindAll("role").Select(claim => claim.Value).ToArray()
        })).WithTags(Tags.Teachers).RequireAuthorization("Teacher");
    }
}
