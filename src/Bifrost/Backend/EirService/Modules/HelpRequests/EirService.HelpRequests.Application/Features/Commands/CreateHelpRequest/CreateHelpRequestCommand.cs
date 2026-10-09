using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Commands.CreateHelpRequest;

public sealed record CreateHelpRequestCommand(string Id, string UserId, string Message) : ICommand<string>;
