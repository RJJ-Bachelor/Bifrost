using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Student.AddStudentMessage;

public sealed record AddStudentMessageCommand(Guid Id, string StudentId, string Text) : ICommand<HelpRequestResult>;
