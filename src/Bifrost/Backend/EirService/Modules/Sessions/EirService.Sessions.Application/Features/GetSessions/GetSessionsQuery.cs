using EirService.Sessions.Application.Features.Common;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.GetSessions;

public sealed record GetSessionsQuery(string TeacherId) : ICommand<IReadOnlyList<SessionResult>>;
