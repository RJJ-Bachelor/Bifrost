using EirService.Sessions.Application.Features.Teacher.Common;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.Teacher.GetSessions;

public sealed record GetSessionsQuery(string TeacherId) : ICommand<IReadOnlyList<SessionResult>>;
