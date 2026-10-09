namespace GnaService.Domain.ValueObjects;

public sealed record NotificationMessages
{
    private NotificationMessages(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static NotificationMessages Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new NotificationMessages(value);
    }
}
