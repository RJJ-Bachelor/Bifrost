using EirService.Sessions.Domain.Entities;

namespace EirService.Sessions.Application.Features.Common;

public sealed record SessionResult(
    Guid Id, string Title, string Code, string CreatedBySubjectId, string? CreatedByDisplayName,
    DateTimeOffset CreatedAtUtc, int CurrentGeneration, DateTimeOffset? LastClearedAtUtc)
{
    public static SessionResult From(Session session) => new(
        session.Id, session.Title, session.Code.Value, session.CreatedBySubjectId,
        session.CreatedByDisplayName, session.CreatedAtUtc, session.CurrentGeneration, session.LastClearedAtUtc);
}
