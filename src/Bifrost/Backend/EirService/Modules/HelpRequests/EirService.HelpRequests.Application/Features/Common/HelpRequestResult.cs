using EirService.HelpRequests.Domain.Entities;
using EirService.Contracts.Sessions.GetSessionTeacherNames;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Common;

public sealed record HelpRequestResult(
    Guid Id, Guid SessionId, int Generation, int RequestNumber, string DisplayNumber,
    string? StudentId, bool IsFaq, HelpType HelpType, HelpRequestStatus Status,
    string? AssignedTeacherId, DateTimeOffset CreatedAt, DateTimeOffset QueuedAt, DateTimeOffset? ReturnedToQueueAt,
    DateTimeOffset? StartedAt, DateTimeOffset? ClosedAt, Guid? FaqAnswerId,
    IReadOnlyList<HelpRequestMessageResult> Messages, string? AssignedTeacherName = null);

public sealed record HelpRequestMessageResult(
    Guid Id, Guid HelpRequestId, string AuthorId, AuthorRole AuthorRole, HelpRequestMessageType MessageType,
    string Text, DateTimeOffset CreatedAt, int SequenceNumber);

public static class HelpRequestMapping
{
    public static async Task<HelpRequestResult> ToResultAsync(this HelpRequest request, CancellationToken cancellationToken)
    {
        if (request.AssignedTeacherId is null) return request.ToResult();
        var names = await new GetSessionTeacherNamesQuery([request.SessionId]).ExecuteAsync(cancellationToken);
        return request.ToResult(names.TryGetValue(request.SessionId, out var teachers)
            ? teachers.GetValueOrDefault(request.AssignedTeacherId) : null);
    }

    public static HelpRequestResult ToResult(this HelpRequest request, string? assignedTeacherName = null) => new(
        request.Id, request.SessionId, request.Generation, request.RequestNumber, request.DisplayNumber,
        request.StudentId, request.IsFaq, request.HelpType, request.Status,
        request.AssignedTeacherId, request.CreatedAt, request.QueuedAt, request.ReturnedToQueueAt,
        request.StartedAt, request.ClosedAt, request.FaqAnswerId,
        request.Messages.Where(message => !message.IsRejected).OrderBy(message => message.SequenceNumber).Select(message => new HelpRequestMessageResult(
            message.Id, message.HelpRequestId, message.AuthorId, message.AuthorRole, message.MessageType, message.Text.Value,
            message.CreatedAt, message.SequenceNumber)).ToArray(), assignedTeacherName);
}
