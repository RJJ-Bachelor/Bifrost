using FastEndpoints;

namespace EirService.Contracts.Participants.RememberSessionTeacher;

public sealed record RememberSessionTeacherCommand(Guid SessionId, string TeacherId, string DisplayName) : ICommand;
