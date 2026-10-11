using EirService.Contracts.Sessions.Common;
using FastEndpoints;

namespace EirService.Participants.Application.Features.JoinSession;

public sealed record JoinSessionCommand(string Code, string StudentId) : ICommand<StudentSessionSummary>;
