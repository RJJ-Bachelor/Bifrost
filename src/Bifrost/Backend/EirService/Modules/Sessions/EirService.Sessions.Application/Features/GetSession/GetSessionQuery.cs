using EirService.Sessions.Application.Features.Common;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.GetSession;

public sealed record GetSessionQuery(Guid SessionId, string TeacherId) : ICommand<SessionResult>;
