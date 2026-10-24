using EirService.Contracts.Exceptions;
using EirService.Contracts.Participants.IsSessionParticipant;
using EirService.Contracts.Participants.IsSessionTeacher;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.Common;

internal static class SessionArchiveAccess
{
    public static async Task EnsureAsync(ISessionRepository repository, Guid sessionId,
        string actorId, bool isTeacher, CancellationToken cancellationToken)
    {
        var session = await repository.GetAsync(sessionId, cancellationToken)
            ?? throw new ResourceNotFoundException();
        var allowed = isTeacher
            ? session.CreatedByTeacherId == actorId ||
                await new IsSessionTeacherQuery(sessionId, actorId).ExecuteAsync(cancellationToken)
            : await new IsSessionParticipantQuery(sessionId, actorId).ExecuteAsync(cancellationToken);
        if (!allowed) throw new ResourceNotFoundException();
    }
}
