using EirService.Participants.Domain.ValueObjects;
using Shared.Domain.Primitives;

namespace EirService.Participants.Domain.Entities;

public sealed class SessionParticipant : Entity<SessionParticipantId>
{
    private SessionParticipant() { }

    public override SessionParticipantId Id
    {
        get => new(SessionId, StudentSubjectId);
        protected set { SessionId = value.SessionId; StudentSubjectId = value.StudentSubjectId; }
    }

    public Guid SessionId { get; private set; }
    public string StudentSubjectId { get; private set; } = null!;
    public DateTimeOffset JoinedAtUtc { get; private set; }

    public static SessionParticipant Join(Guid sessionId, string studentSubjectId, DateTimeOffset now)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("A session id is required.", nameof(sessionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(studentSubjectId);
        return new SessionParticipant { SessionId = sessionId, StudentSubjectId = studentSubjectId, JoinedAtUtc = now.ToUniversalTime() };
    }
}
