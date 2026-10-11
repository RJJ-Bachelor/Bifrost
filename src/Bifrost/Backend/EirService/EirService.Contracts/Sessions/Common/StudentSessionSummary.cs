namespace EirService.Contracts.Sessions.Common;

public sealed record StudentSessionSummary(
    Guid Id, string Title, DateTimeOffset CreatedAtUtc,
    int CurrentGeneration, DateTimeOffset? LastClearedAtUtc);
