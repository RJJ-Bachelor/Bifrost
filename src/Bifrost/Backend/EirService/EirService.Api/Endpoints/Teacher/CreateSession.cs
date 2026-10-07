namespace EirService.Api.Endpoints.Teacher;

internal static class CreateSession
{
    public static void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/teachers/createsession", () => Results.Ok(true))
            .WithName("CreateSession")
            .WithTags(Tags.Teachers)
            .RequireAuthorization("Teacher");
    }
}
