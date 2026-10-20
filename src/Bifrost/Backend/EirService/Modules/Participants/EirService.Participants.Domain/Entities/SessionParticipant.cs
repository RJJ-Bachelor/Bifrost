using EirService.Participants.Domain.ValueObjects;
using Shared.Domain.Primitives;

namespace EirService.Participants.Domain.Entities;

public sealed class SessionParticipant : Entity<SessionParticipantId>
{
    private SessionParticipant() { }

    public override SessionParticipantId Id
    {
        get => new(SessionId, SubjectId, Role);
        protected set { SessionId = value.SessionId; SubjectId = value.SubjectId; Role = value.Role; }
    }

    public Guid SessionId { get; private set; }
    public string SubjectId { get; private set; } = null!;
    public ParticipantRole Role { get; private set; }
    public DateTimeOffset JoinedAtUtc { get; private set; }

    public static SessionParticipant Join(Guid sessionId, string subjectId, ParticipantRole role, DateTimeOffset now)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("A session id is required.", nameof(sessionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        if (subjectId.Length > 100) throw new ArgumentException("A subject id cannot exceed 100 characters.", nameof(subjectId));
        if (!Enum.IsDefined(role)) throw new ArgumentException("Unknown participant role.", nameof(role));
        return new SessionParticipant { SessionId = sessionId, SubjectId = subjectId, Role = role, JoinedAtUtc = now.ToUniversalTime() };
    }
}
