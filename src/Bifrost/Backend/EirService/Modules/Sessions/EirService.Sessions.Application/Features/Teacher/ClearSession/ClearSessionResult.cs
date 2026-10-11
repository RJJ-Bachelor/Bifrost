namespace EirService.Sessions.Application.Features.Teacher.ClearSession;

public sealed record ClearSessionResult(Guid SessionId, int CurrentGeneration, DateTimeOffset LastClearedAtUtc);
