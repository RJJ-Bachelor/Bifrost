using FastEndpoints;

namespace EirService.Sessions.Application.Features.Teacher.GetSessions;

public sealed record GetSessionsQuery(string TeacherId) : ICommand<IReadOnlyList<SessionListResult>>;
