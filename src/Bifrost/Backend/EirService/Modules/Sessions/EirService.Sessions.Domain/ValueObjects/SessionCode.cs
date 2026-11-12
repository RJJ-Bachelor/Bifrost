using Shared.Domain.ValueObjects;

namespace EirService.Sessions.Domain.ValueObjects;

public sealed record SessionCode
{
    public const int Length = ShortCode.Length;

    public string Value { get; }

    public SessionCode(string value)
    {
        Value = new ShortCode(value).Value;
    }

    public static SessionCode Generate() => new(ShortCode.Generate().Value);

    public static bool IsValid(string? value) => ShortCode.IsValid(value);

    public override string ToString() => Value;
}
