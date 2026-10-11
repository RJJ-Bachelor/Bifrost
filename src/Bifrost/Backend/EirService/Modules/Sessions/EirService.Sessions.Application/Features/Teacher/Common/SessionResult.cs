using EirService.Sessions.Domain.Entities;

namespace EirService.Sessions.Application.Features.Teacher.Common;

public sealed record SessionResult(
    Guid Id, string Title, string Code, string CreatedByTeacherId, string? CreatedByDisplayName,
    DateTimeOffset CreatedAtUtc, int CurrentGeneration, DateTimeOffset? LastClearedAtUtc)
{
    public static SessionResult From(Session session) => new(
        session.Id, session.Title, session.Code.Value, session.CreatedByTeacherId,
        session.CreatedByDisplayName, session.CreatedAtUtc, session.CurrentGeneration, session.LastClearedAtUtc);
}
