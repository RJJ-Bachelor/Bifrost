using FastEndpoints;

namespace EirService.Contracts.Participants.GetSessionTeachers;

public sealed record GetSessionTeachersQuery(Guid[] SessionIds) : ICommand<IReadOnlyList<SessionTeacher>>;

public sealed record SessionTeacher(Guid SessionId, string TeacherId, string? DisplayName);
