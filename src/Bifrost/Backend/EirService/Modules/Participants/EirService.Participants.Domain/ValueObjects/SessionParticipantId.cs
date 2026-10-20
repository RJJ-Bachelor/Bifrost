namespace EirService.Participants.Domain.ValueObjects;

public readonly record struct SessionParticipantId(Guid SessionId, string SubjectId, Entities.ParticipantRole Role);
