namespace Shared.Application.Messaging;

public interface IGeneralizedMessagePublisher
{
    Task PublishGeneralizedCreatedAsync(
        string id,
        string message,
        CancellationToken cancellationToken = default);
}
