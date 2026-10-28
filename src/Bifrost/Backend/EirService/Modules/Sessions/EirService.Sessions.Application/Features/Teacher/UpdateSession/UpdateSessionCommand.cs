using EirService.Sessions.Application.Features.Teacher.Common;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.Teacher.UpdateSession;

public sealed record UpdateSessionCommand(Guid SessionId, string TeacherId, string Title) : ICommand<SessionResult>;
