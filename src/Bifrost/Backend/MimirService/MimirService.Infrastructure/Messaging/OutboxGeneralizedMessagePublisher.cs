using MimirService.Application.Services.Messaging;
using MimirService.Infrastructure.Persistence;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace MimirService.Infrastructure.Messaging;

public sealed class OutboxGeneralizedMessagePublisher(MimirRequestDbContext dbContext, TimeProvider clock) : IGeneralizedMessagePublisher
{
    public async Task PublishAsync(GeneralizedCreatedMessage message, CancellationToken cancellationToken)
    {
        dbContext.Outbox.Add(OutboxMessage.Create(new OutgoingMessage(message.GeneralizationId, message,
            MessageRoutes.Requests, MessageRoutes.GeneralizedCreated), clock.GetUtcNow()));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
