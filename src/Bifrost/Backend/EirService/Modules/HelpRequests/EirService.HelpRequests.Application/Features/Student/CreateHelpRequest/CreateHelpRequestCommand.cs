using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Student.CreateHelpRequest;

public sealed record CreateHelpRequestCommand(string Id, string UserId, string Message, Guid? SessionId = null, int? Generation = null) : ICommand<string>;
