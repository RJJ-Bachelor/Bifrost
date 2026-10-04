namespace EirService.Requests.Application.Messaging;

public interface IRequestMessagePublisher
{
    Task PublishRequestCreatedAsync(
        string id,
        string message,
        CancellationToken cancellationToken = default);
}
