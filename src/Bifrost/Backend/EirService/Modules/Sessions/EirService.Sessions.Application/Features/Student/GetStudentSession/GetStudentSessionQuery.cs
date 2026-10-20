using EirService.Contracts.Sessions.Common;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.Student.GetStudentSession;

public sealed record GetStudentSessionQuery(Guid SessionId, string StudentId) : ICommand<SessionSummary>;
