using FastEndpoints;

namespace EirService.Participants.Application.Features.Student.RedeemIdentityTransferCode;

public sealed record RedeemIdentityTransferCodeCommand(string Code) : ICommand<string>;
