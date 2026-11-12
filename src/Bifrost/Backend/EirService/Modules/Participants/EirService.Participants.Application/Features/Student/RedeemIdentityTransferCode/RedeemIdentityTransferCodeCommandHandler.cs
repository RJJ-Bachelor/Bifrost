using EirService.Contracts.Exceptions;
using EirService.Participants.Application.Repositories;
using FastEndpoints;
using Shared.Domain.ValueObjects;

namespace EirService.Participants.Application.Features.Student.RedeemIdentityTransferCode;

public sealed class RedeemIdentityTransferCodeCommandHandler(
    IStudentIdentityTransferRepository repository, TimeProvider clock)
    : ICommandHandler<RedeemIdentityTransferCodeCommand, string>
{
    public async Task<string> ExecuteAsync(RedeemIdentityTransferCodeCommand command, CancellationToken cancellationToken) =>
        await repository.RedeemAsync(new ShortCode(command.Code).Value, clock.GetUtcNow(), cancellationToken)
        ?? throw new ResourceNotFoundException();
}
