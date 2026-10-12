using FastEndpoints;

namespace EirService.Contracts.Participants.IsSessionParticipant;

public sealed record IsSessionParticipantQuery(Guid SessionId, string StudentSubjectId) : ICommand<bool>;
