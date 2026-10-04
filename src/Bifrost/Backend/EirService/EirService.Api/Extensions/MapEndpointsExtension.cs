namespace EirService.Api.Extensions
{
    public static class MapEndpointsExtension
    {
        public static void MapEndpoints(this WebApplication app)
        {
            var api = app.MapGroup("/api");

            app.MapDefaultEndpoints();
            Endpoints.Student.Request.MapEndpoint(api);
            Endpoints.Student.Test.MapEndpoint(api);
            Endpoints.Teacher.Test.MapEndpoint(api);
        }
    }
}
