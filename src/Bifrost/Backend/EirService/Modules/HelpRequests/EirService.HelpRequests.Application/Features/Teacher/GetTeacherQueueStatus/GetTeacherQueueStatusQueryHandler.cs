using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Teacher.GetTeacherQueueStatus;

public sealed class GetTeacherQueueStatusQueryHandler(
    IHelpRequestRepository repository, HelpRequestAccess access, TimeProvider timeProvider)
    : ICommandHandler<GetTeacherQueueStatusQuery, TeacherQueueStatusResult>
{
    public async Task<TeacherQueueStatusResult> ExecuteAsync(GetTeacherQueueStatusQuery query, CancellationToken cancellationToken)
    {
        var session = await access.ForSessionAsync(query.SessionId, query.TeacherId, AuthorRole.Teacher, cancellationToken);
        var requests = await repository.GetSessionActivityAsync(session.Id, session.Generation, cancellationToken);
        var waiting = requests.Where(request => request.Status == HelpRequestStatus.Pending).ToArray();
        double? frontWait = waiting.Length == 0 ? null :
            Math.Max(0, (timeProvider.GetUtcNow() - waiting[0].QueuedAt).TotalMinutes);
        var myReservedRequestId = requests.Where(request => request.AssignedTeacherId == query.TeacherId &&
                request.Status is HelpRequestStatus.Pending or HelpRequestStatus.Active)
            .OrderByDescending(request => request.Status == HelpRequestStatus.Active)
            .ThenBy(request => request.QueuedAt).ThenBy(request => request.Id)
            .Select(request => (Guid?)request.Id).FirstOrDefault();
        return new TeacherQueueStatusResult(waiting.Length,
            waiting.Count(request => request.AssignedTeacherId is null), frontWait, session.Generation, myReservedRequestId);
    }
}
