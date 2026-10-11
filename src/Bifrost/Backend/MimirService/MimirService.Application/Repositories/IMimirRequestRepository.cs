using MimirService.Domain.Entities;
using Shared.Application.Messaging.Outbox;

namespace MimirService.Application.Repositories;

public interface IMimirRequestRepository
{
    Task<bool> TryAddAsync(MimirRequest request, OutgoingMessage reply, CancellationToken cancellationToken);
}
