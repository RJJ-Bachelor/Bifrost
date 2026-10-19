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
        if (value.Length > 100) throw new ArgumentException("User IDs cannot exceed 100 characters.", nameof(value));
        return new UserId(value);
    }
}
