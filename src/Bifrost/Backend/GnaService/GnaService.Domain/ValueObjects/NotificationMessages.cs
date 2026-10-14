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
        if (value.Length > 4000) throw new ArgumentException("Notifications cannot exceed 4000 characters.", nameof(value));
        return new NotificationMessages(value);
    }
}
