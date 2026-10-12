using EirService.Sessions.Application.Features.Teacher.Common;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.Teacher.CreateSession;

public sealed record CreateSessionCommand(string TeacherId, string Title, string? DisplayName = null) : ICommand<SessionResult>;
