using FastEndpoints;

namespace EirService.Participants.Application.Features.Teacher.JoinSession;

public sealed record JoinTeacherSessionCommand(string Code, string TeacherId, string? DisplayName = null) : ICommand<Guid>;
