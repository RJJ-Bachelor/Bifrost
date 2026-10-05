using GnaService.Domain.Entities;

namespace GnaService.Application.Repositories;

public interface INotificationRepository
{
    Task AddAsync(
        Notification notification,
        CancellationToken cancellationToken);
}
