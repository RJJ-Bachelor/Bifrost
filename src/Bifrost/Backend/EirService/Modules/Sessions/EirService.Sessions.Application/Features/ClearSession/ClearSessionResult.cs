namespace EirService.Sessions.Application.Features.ClearSession;

public sealed record ClearSessionResult(Guid SessionId, int CurrentGeneration, DateTimeOffset LastClearedAtUtc);
