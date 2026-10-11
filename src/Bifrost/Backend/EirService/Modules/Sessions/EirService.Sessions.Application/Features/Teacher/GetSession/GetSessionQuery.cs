using EirService.Sessions.Application.Features.Teacher.Common;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.Teacher.GetSession;

public sealed record GetSessionQuery(Guid SessionId, string TeacherId) : ICommand<SessionResult>;
