namespace GnaService.Domain.ValueObjects;

public sealed record UserId
{
    private UserId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static UserId Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new UserId(value);
    }
}
