namespace EirService.Api.Extensions
{
    public static class MapEndpointsExtension
    {
        public static void MapEndpoints(this WebApplication app)
        {
            var api = app.MapGroup("/api");

            app.MapDefaultEndpoints();
            Endpoints.Alive.MapEndpoint(api);
            Endpoints.Student.Identity.MapEndpoint(api);
            Endpoints.Teacher.Identity.MapEndpoint(api);
        }
    }
}
