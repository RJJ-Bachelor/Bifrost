using EirService.HelpRequests.Application.Features.Common;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Teacher.TakeNextHelpRequest;

public sealed record TakeNextHelpRequestCommand(Guid SessionId, string TeacherId) : ICommand<HelpRequestResult?>;
