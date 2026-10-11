using EirService.Sessions.Application.Features.Common;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.CreateSession;

public sealed record CreateSessionCommand(string TeacherId, string Title, string? DisplayName = null) : ICommand<SessionResult>;
