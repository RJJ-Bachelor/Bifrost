using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using FastEndpoints;

namespace EirService.Participants.Application.Features.Student.CreateIdentityTransferCode;

public sealed class CreateIdentityTransferCodeCommandHandler(
    IStudentIdentityTransferRepository repository, TimeProvider clock)
    : ICommandHandler<CreateIdentityTransferCodeCommand, CreateIdentityTransferCodeResult>
{
    public async Task<CreateIdentityTransferCodeResult> ExecuteAsync(
        CreateIdentityTransferCodeCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var transfer = StudentIdentityTransfer.Create(command.StudentId, now);
            if (await repository.TryAddAsync(transfer, cancellationToken))
                return new CreateIdentityTransferCodeResult(transfer.Code, transfer.ExpiresAtUtc);
        }

        throw new InvalidOperationException("A unique identity transfer code could not be allocated.");
    }
}
