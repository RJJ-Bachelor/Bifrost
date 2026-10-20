using EirService.Contracts.Sessions.Common;
using FastEndpoints;

namespace EirService.Participants.Application.Features.Student.GetStudentSessions;

public sealed record GetStudentSessionsQuery(string StudentId) : ICommand<IReadOnlyList<SessionSummary>>;
