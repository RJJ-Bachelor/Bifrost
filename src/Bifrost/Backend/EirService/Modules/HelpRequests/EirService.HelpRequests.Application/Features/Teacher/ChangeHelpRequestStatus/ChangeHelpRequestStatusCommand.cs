using EirService.HelpRequests.Application.Features.Common;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Teacher.ChangeHelpRequestStatus;

public enum HelpRequestAction { Start, Complete, Cancel }

public sealed record ChangeHelpRequestStatusCommand(Guid Id, string TeacherId, HelpRequestAction? Action)
    : ICommand<HelpRequestResult>;
