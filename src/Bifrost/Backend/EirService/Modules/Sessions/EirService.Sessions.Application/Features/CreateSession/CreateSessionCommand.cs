using FastEndpoints;

namespace EirService.Sessions.Application.Features.CreateSession;

public sealed record CreateSessionCommand(string TeacherId) : ICommand<bool>;
