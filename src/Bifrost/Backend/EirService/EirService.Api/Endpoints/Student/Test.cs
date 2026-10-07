namespace EirService.Api.Endpoints.Student
{
    internal static class Test
    {
        public sealed record Request(string Id, string Message);

        public static void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/students/sendmessages", TestFunk)
                .WithTags(Tags.Students)
                .RequireAuthorization("Student");
        }

        private static IResult TestFunk(Request request)
        {
            return Results.Ok($"Result: {request.Id} - {request.Message}");
        }
    }
}
