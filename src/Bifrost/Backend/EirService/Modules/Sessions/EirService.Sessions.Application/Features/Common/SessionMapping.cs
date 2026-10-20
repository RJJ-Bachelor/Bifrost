using EirService.Contracts.Sessions.Common;
using EirService.Sessions.Domain.Entities;

namespace EirService.Sessions.Application.Features.Common;

internal static class SessionMapping
{
    public static SessionSummary ToSummary(this Session session) => new(
        session.Id, session.Title, session.CreatedAtUtc, session.CurrentGeneration, session.LastClearedAtUtc);
}
