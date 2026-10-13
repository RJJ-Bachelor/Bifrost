namespace EirService.HelpRequests.Domain.ValueObjects;

public sealed record MessageText
{
    public const int MaxLength = 4000;
    public string Value { get; }

    public MessageText(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaxLength) throw new ArgumentException($"Messages cannot exceed {MaxLength} characters.", nameof(value));
        Value = value.Trim();
    }
}
