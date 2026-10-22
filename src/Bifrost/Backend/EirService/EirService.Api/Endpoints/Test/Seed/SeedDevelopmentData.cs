using EirService.Api.Authentication;
using EirService.Api.Development;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Test.Seed;

public static class SeedDevelopmentData
{
    public static void MapDevelopmentSeedApi(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        app.MapPost("/api/test/seed", async (SeedDevelopmentDataRequest request, HttpContext context,
            DevelopmentDataSeeder seeder, CancellationToken cancellationToken) =>
        {
            if (request.Scenario != DevelopmentDataSeeder.Scenario || request.StudentCount is < 2 or > DevelopmentDataSeeder.MaxStudents)
                return Results.BadRequest(new { message = $"Use scenario '{DevelopmentDataSeeder.Scenario}' and 2-{DevelopmentDataSeeder.MaxStudents} students." });
            var teacherId = context.User.GetRequiredUserId();
            if (string.IsNullOrWhiteSpace(teacherId)) return Results.Forbid();
            var result = await seeder.SeedAsync(teacherId, context.User.FindFirst("name")?.Value,
                request.StudentCount, cancellationToken);
            return Results.Created($"/api/teachers/sessions/{result.Session.Id}", result);
        }).RequireAuthorization(EndpointTags.Teacher).WithName("SeedDevelopmentData").WithTags("Test");
    }
}
