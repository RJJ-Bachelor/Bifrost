using EirService.Api.Development;

namespace EirService.Api.Endpoints.Test.Seed;

public sealed class SeedDevelopmentDataRequest
{
    public string Scenario { get; set; } = DevelopmentDataSeeder.Scenario;
    public int StudentCount { get; set; } = 6;
}
