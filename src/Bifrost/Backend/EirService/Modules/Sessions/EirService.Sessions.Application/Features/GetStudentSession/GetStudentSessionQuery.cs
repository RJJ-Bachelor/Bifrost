using EirService.Contracts.Sessions.Common;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.GetStudentSession;

public sealed record GetStudentSessionQuery(Guid SessionId, string StudentId) : ICommand<StudentSessionSummary>;
