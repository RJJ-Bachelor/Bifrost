using FastEndpoints;

namespace EirService.Contracts.Participants.GetSessionParticipantIds;

public sealed record GetSessionParticipantIdsQuery(Guid SessionId) : ICommand<string[]>;