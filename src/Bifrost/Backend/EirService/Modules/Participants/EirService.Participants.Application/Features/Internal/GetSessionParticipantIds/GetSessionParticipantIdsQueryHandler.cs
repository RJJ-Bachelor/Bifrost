using EirService.Contracts.Participants.GetSessionParticipantIds;
using EirService.Participants.Application.Repositories;
using FastEndpoints;

namespace EirService.Participants.Application.Features.Internal.GetSessionParticipantIds;

public sealed class GetSessionParticipantIdsQueryHandler(IParticipantRepository repository)
    : ICommandHandler<GetSessionParticipantIdsQuery, string[]>
{
    public Task<string[]> ExecuteAsync(GetSessionParticipantIdsQuery query, CancellationToken cancellationToken) =>
        repository.GetParticipantIdsAsync(query.SessionId, cancellationToken);
}