namespace EirService.HelpRequests.Application.Features.Student.GetHelpRequests;

public sealed record HelpRequestResult(Guid Id, string? UserId, string Message, Guid? SessionId = null, int? Generation = null);
