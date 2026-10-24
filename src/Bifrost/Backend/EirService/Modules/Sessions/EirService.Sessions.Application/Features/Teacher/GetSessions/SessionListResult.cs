using EirService.Sessions.Domain.Entities;

namespace EirService.Sessions.Application.Features.Teacher.GetSessions;

public sealed record SessionListResult(
    Guid Id, string Title, string Code, string TeacherCode, string CreatedByTeacherId, string? CreatedByDisplayName,
    DateTimeOffset CreatedAtUtc, int CurrentGeneration, DateTimeOffset? LastClearedAtUtc, int WaitingCount)
{
    public static SessionListResult From(Session session, int waitingCount) => new(
        session.Id, session.Title, session.Code.Value, session.TeacherCode.Value, session.CreatedByTeacherId,
        session.CreatedByDisplayName, session.CreatedAtUtc, session.CurrentGeneration, session.LastClearedAtUtc, waitingCount);
}
