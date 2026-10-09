namespace EirService.HelpRequests.Application.Features.Queries.GetHelpRequests;

public sealed record HelpRequestResult(string Id, string? UserId, string Message);
