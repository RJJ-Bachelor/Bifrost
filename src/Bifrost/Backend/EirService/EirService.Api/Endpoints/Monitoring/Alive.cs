namespace EirService.Api.Endpoints
{
    internal class Alive
    {
        public static void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/monitoring/alive", AliveHandler)
                .WithTags("Monitoring")
                .AllowAnonymous();
        }

        private static IResult AliveHandler()
        {
            return Results.Ok($"EirService is alive and running.");
        }
    }
}
