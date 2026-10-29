using EirService.HelpRequests.Application.Features.Common;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Teacher.AnswerHelpRequest;

public sealed record AnswerHelpRequestCommand(Guid Id, string TeacherId, string Text, string? Question = null) : ICommand<HelpRequestResult>;
