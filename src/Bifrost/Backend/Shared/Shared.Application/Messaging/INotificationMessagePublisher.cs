namespace Shared.Application.Messaging;

public interface INotificationMessagePublisher
{
    Task PublishNotificationCreatedAsync(
        string id,
        string userId,
        string messages,
        CancellationToken cancellationToken = default);
}
