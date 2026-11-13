namespace EirService.Participants.Application.Features.Student.CreateIdentityTransferCode;

public sealed record CreateIdentityTransferCodeResult(string Code, DateTimeOffset ExpiresAtUtc);
