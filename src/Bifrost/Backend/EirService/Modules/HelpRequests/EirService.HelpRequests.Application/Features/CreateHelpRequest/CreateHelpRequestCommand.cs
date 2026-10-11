using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.CreateHelpRequest;

public sealed record CreateHelpRequestCommand(string Id, string UserId, string Message, Guid? SessionId = null, int? Generation = null) : ICommand<string>;
