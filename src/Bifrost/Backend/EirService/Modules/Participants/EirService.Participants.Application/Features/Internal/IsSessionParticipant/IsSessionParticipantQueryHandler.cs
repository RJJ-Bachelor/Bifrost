using EirService.Contracts.Participants.IsSessionParticipant;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using FastEndpoints;

namespace EirService.Participants.Application.Features.Internal.IsSessionParticipant;

public sealed class IsSessionParticipantQueryHandler(IParticipantRepository repository)
    : ICommandHandler<IsSessionParticipantQuery, bool>
{
    public Task<bool> ExecuteAsync(IsSessionParticipantQuery query, CancellationToken cancellationToken) =>
        repository.IsParticipantAsync(query.SessionId, query.StudentSubjectId, ParticipantRole.Student, cancellationToken);
}
