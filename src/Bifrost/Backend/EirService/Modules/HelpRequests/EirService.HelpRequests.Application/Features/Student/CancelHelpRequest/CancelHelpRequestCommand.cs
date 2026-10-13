using EirService.HelpRequests.Application.Features.Common;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Student.CancelHelpRequest;

public sealed record CancelHelpRequestCommand(Guid Id, string StudentId, string Action) : ICommand<HelpRequestResult>;
