using EirService.HelpRequests.Domain.Entities;

namespace EirService.HelpRequests.Application.Features.Common;

public sealed record HelpRequestResult(
    Guid Id, Guid SessionId, int Generation, int RequestNumber, string DisplayNumber,
    string StudentId, HelpType HelpType, HelpRequestStatus Status,
    string? AssignedTeacherId, DateTimeOffset CreatedAt, DateTimeOffset QueuedAt, DateTimeOffset? ReturnedToQueueAt,
    DateTimeOffset? StartedAt, DateTimeOffset? ClosedAt, Guid? FaqAnswerId,
    IReadOnlyList<HelpRequestMessageResult> Messages);

public sealed record HelpRequestMessageResult(
    Guid Id, Guid HelpRequestId, string AuthorId, AuthorRole AuthorRole, string Text, DateTimeOffset CreatedAt, int SequenceNumber);

public static class HelpRequestMapping
{
    public static HelpRequestResult ToResult(this HelpRequest request) => new(
        request.Id, request.SessionId, request.Generation, request.RequestNumber, request.DisplayNumber,
        request.StudentId, request.HelpType, request.Status,
        request.AssignedTeacherId, request.CreatedAt, request.QueuedAt, request.ReturnedToQueueAt,
        request.StartedAt, request.ClosedAt, request.FaqAnswerId,
        request.Messages.OrderBy(message => message.SequenceNumber).Select(message => new HelpRequestMessageResult(
            message.Id, message.HelpRequestId, message.AuthorId, message.AuthorRole, message.Text.Value,
            message.CreatedAt, message.SequenceNumber)).ToArray());
}
