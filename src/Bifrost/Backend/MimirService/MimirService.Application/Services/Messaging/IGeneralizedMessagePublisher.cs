using Shared.Application.Messaging.Contracts;

namespace MimirService.Application.Services.Messaging;

public interface IGeneralizedMessagePublisher
{
    Task PublishAsync(GeneralizedCreatedMessage message, CancellationToken cancellationToken);
}
