using EirService.Participants.Domain.Entities;

namespace EirService.Participants.Application.Repositories;

public interface IStudentIdentityTransferRepository
{
    Task<bool> TryAddAsync(StudentIdentityTransfer transfer, CancellationToken cancellationToken);
    Task<string?> RedeemAsync(string code, DateTimeOffset now, CancellationToken cancellationToken);
}
