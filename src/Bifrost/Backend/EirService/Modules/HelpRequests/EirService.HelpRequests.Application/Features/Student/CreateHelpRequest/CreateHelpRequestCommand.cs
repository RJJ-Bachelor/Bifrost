using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Student.CreateHelpRequest;

public sealed record CreateHelpRequestCommand(Guid SessionId, string StudentId, string Message, HelpType? HelpType)
    : ICommand<HelpRequestResult>;
