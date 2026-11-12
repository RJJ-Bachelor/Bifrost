using System.Security.Cryptography;

namespace Shared.Domain.ValueObjects;

public sealed record ShortCode
{
    public const int Length = 8;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public string Value { get; }

    public ShortCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!IsValid(value))
            throw new ArgumentException("A code must contain eight letters or digits from the code alphabet.", nameof(value));

        Value = value.Trim().ToUpperInvariant();
    }

    public static ShortCode Generate() => new(new string(
        Enumerable.Range(0, Length).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray()));

    public static bool IsValid(string? value) => value is not null &&
        value.Trim().Length == Length && value.Trim().ToUpperInvariant().All(Alphabet.Contains);

    public override string ToString() => Value;
}
