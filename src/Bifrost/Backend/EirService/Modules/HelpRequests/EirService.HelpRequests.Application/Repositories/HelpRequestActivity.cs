using EirService.HelpRequests.Domain.Entities;

namespace EirService.HelpRequests.Application.Repositories;

// A read projection for queue status and statistics, without conversation messages.
public sealed record HelpRequestActivity(
    Guid Id, string? StudentId, int RequestNumber, HelpType HelpType, HelpRequestStatus Status,
    string? AssignedTeacherId, DateTimeOffset QueuedAt, DateTimeOffset? StartedAt, DateTimeOffset? ClosedAt)
{
    public bool IsOpen => Status is HelpRequestStatus.Pending or HelpRequestStatus.Active or
        HelpRequestStatus.AwaitingFaq or HelpRequestStatus.AwaitingConfirmation;
    public string DisplayNumber => $"{(StudentId is null ? "FAQ" : "BF")}-{RequestNumber:D3}";
}
