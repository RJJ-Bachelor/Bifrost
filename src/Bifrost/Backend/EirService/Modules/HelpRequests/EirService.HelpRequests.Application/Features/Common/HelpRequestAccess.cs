using EirService.Contracts.Exceptions;
using EirService.Contracts.Participants.IsSessionParticipant;
using EirService.Contracts.Sessions.GetHelpRequestSession;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Common;

public sealed class HelpRequestAccess(IHelpRequestRepository repository)
{
    public async Task<HelpRequestSession> ForSessionAsync(Guid sessionId, string actorId, AuthorRole role, CancellationToken cancellationToken)
    {
        var session = await new GetHelpRequestSessionQuery(sessionId).ExecuteAsync(cancellationToken)
            ?? throw new ResourceNotFoundException();

        var allowed = role switch
        {
            AuthorRole.Teacher => session.TeacherIds.Contains(actorId, StringComparer.Ordinal),
            AuthorRole.Student => await new IsSessionParticipantQuery(sessionId, actorId).ExecuteAsync(cancellationToken),
            _ => false
        };

        if (!allowed) throw new ResourceNotFoundException();

        return session;
    }

    public async Task<HelpRequest> ForRequestAsync(Guid id, string actorId, AuthorRole role,
        bool requireCurrentGeneration, CancellationToken cancellationToken)
    {
        var request = await repository.GetAsync(id, cancellationToken) ?? throw new ResourceNotFoundException();

        if (role == AuthorRole.Student && request.StudentId != actorId) throw new ResourceNotFoundException();
        
        var session = await ForSessionAsync(request.SessionId, actorId, role, cancellationToken);
        
        if (requireCurrentGeneration && session.Generation != request.Generation)
            throw new OperationConflictException("The session has been cleared. This request belongs to an earlier generation.");
        
        return request;
    }
}
