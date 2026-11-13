using FastEndpoints;

namespace EirService.Participants.Application.Features.Student.CreateIdentityTransferCode;

public sealed record CreateIdentityTransferCodeCommand(string StudentId) : ICommand<CreateIdentityTransferCodeResult>;
