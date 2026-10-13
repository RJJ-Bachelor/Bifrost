using EirService.HelpRequests.Application.Features.Common;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Student.GetStudentHelpRequest;

public sealed record GetStudentHelpRequestQuery(Guid Id, string StudentId) : ICommand<HelpRequestResult>;
