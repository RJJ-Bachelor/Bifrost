using FastEndpoints;

namespace EirService.Sessions.Application.Features.Teacher.DeleteSession;

public sealed record DeleteSessionCommand(Guid SessionId, string TeacherId) : ICommand;
