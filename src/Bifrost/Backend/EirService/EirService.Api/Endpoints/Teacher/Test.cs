namespace EirService.Api.Endpoints.Teacher
{
    internal static class Test
    {
        public sealed record Request(string Id, string Message);

        public static void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/teachers/sendmessages", TestFunk)
                .WithTags(Tags.Teachers);
        }

        private static IResult TestFunk(Request request)
        {
            return Results.Ok($"Result: {request.Id} - {request.Message}");
        }
    }
}
