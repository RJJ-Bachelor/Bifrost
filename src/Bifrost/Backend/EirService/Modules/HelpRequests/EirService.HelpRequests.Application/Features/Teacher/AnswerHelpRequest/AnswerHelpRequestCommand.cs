using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Teacher.AnswerHelpRequest;

public sealed record AnswerHelpRequestCommand(Guid Id, string TeacherId, string Text) : ICommand<HelpRequestResult>;
