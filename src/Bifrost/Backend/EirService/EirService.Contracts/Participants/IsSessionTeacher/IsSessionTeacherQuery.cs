using FastEndpoints;

namespace EirService.Contracts.Participants.IsSessionTeacher;

public sealed record IsSessionTeacherQuery(Guid SessionId, string TeacherId) : ICommand<bool>;
