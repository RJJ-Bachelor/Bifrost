using System.Security.Cryptography;

namespace EirService.Sessions.Domain.ValueObjects;

public sealed record SessionCode
{
    public const int Length = 8;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public string Value { get; }

    public SessionCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != Length || normalized.Any(character => !Alphabet.Contains(character)))
            throw new ArgumentException("A session code must contain eight letters or digits from the session alphabet.", nameof(value));

        Value = normalized;
    }

    public static SessionCode Generate() => new(new string(
        Enumerable.Range(0, Length).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray()));

    public static bool IsValid(string? value) => value is not null &&
        value.Trim().Length == Length && value.Trim().ToUpperInvariant().All(Alphabet.Contains);

    public override string ToString() => Value;
}
