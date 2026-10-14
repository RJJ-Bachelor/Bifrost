using GnaService.Domain.Entities;

namespace GnaService.Application.Repositories;

public interface INotificationRepository
{
    Task<bool> TryAddAsync(
        Notification notification,
        CancellationToken cancellationToken);
}
