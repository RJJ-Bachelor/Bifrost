using EirService.Contracts.Sessions.GetHelpRequestSession;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;
using EirService.Contracts.Participants.GetSessionTeacherIds;

namespace EirService.Sessions.Application.Features.Internal.GetHelpRequestSession;

public sealed class GetHelpRequestSessionQueryHandler(
    ISessionRepository repository, ICommandHandler<GetSessionTeacherIdsQuery, string[]> teachers)
    : ICommandHandler<GetHelpRequestSessionQuery, HelpRequestSession?>
{
    public async Task<HelpRequestSession?> ExecuteAsync(GetHelpRequestSessionQuery query, CancellationToken cancellationToken)
    {
        var session = await repository.GetAsync(query.SessionId, cancellationToken);
        if (session is null) return null;
        var joinedTeachers = await teachers.ExecuteAsync(new GetSessionTeacherIdsQuery(session.Id), cancellationToken);
        // The creator is always a teacher, including sessions created before teacher membership existed.
        var teacherIds = joinedTeachers.Prepend(session.CreatedByTeacherId).Distinct(StringComparer.Ordinal).ToArray();
        return new HelpRequestSession(session.Id, session.CurrentGeneration, teacherIds);
    }
}
