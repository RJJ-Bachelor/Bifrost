using EirService.Contracts.Participants.GetSessionTeachers;
using EirService.Contracts.Sessions.GetSessionTeacherNames;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.Internal.GetSessionTeacherNames;

public sealed class GetSessionTeacherNamesQueryHandler(ISessionRepository repository)
    : ICommandHandler<GetSessionTeacherNamesQuery, IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string?>>>
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string?>>> ExecuteAsync(
        GetSessionTeacherNamesQuery query, CancellationToken cancellationToken)
    {
        if (query.SessionIds.Length == 0) return new Dictionary<Guid, IReadOnlyDictionary<string, string?>>();
        var sessions = await repository.GetByIdsAsync(query.SessionIds, cancellationToken);
        if (sessions.Count == 0) return new Dictionary<Guid, IReadOnlyDictionary<string, string?>>();
        var teachers = await new GetSessionTeachersQuery(sessions.Select(session => session.Id).ToArray()).ExecuteAsync(cancellationToken);
        var grouped = teachers.ToLookup(teacher => teacher.SessionId);
        return sessions.ToDictionary(session => session.Id, session =>
        {
            var names = grouped[session.Id].ToDictionary(teacher => teacher.TeacherId, teacher => teacher.DisplayName,
                StringComparer.Ordinal);
            if (!names.TryGetValue(session.CreatedByTeacherId, out var creatorName) || string.IsNullOrWhiteSpace(creatorName))
                names[session.CreatedByTeacherId] = session.CreatedByDisplayName;
            return (IReadOnlyDictionary<string, string?>)names;
        });
    }
}
