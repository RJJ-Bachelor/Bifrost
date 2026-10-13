using EirService.HelpRequests.Application.Features.Common;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Teacher.GetHelpRequest;

public sealed record GetHelpRequestQuery(Guid Id, string TeacherId) : ICommand<HelpRequestResult>;
