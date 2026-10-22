using EirService.HelpRequests.Application.Features.Common;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Student.RespondToFaq;

public sealed record RespondToFaqCommand(Guid Id, string StudentId, bool? Accept) : ICommand<HelpRequestResult>;
