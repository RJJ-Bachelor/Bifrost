namespace EirService.Contracts.Sessions.Common;

public sealed record SessionSummary(
    Guid Id, string Title, DateTimeOffset CreatedAtUtc,
    int CurrentGeneration, DateTimeOffset? LastClearedAtUtc);
