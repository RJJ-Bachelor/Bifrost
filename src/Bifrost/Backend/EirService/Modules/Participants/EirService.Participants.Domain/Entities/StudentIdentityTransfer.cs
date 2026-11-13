using Shared.Domain.Primitives;
using Shared.Domain.ValueObjects;

namespace EirService.Participants.Domain.Entities;

public sealed class StudentIdentityTransfer : Entity<string>
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private StudentIdentityTransfer() { }

    public override string Id
    {
        get => Code;
        protected set => Code = value;
    }

    public string Code { get; private set; } = null!;
    public string StudentId { get; private set; } = null!;
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public static StudentIdentityTransfer Create(string studentId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);
        if (studentId.Length > 100) throw new ArgumentException("A student id cannot exceed 100 characters.", nameof(studentId));

        return new StudentIdentityTransfer
        {
            Code = ShortCode.Generate().Value,
            StudentId = studentId,
            ExpiresAtUtc = now.ToUniversalTime().Add(Lifetime)
        };
    }
}
