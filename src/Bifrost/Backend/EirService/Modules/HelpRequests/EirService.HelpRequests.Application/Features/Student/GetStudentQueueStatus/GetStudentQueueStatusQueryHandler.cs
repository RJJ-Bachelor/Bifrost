using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Student.GetStudentQueueStatus;

public sealed class GetStudentQueueStatusQueryHandler(IHelpRequestRepository repository, HelpRequestAccess access)
    : ICommandHandler<GetStudentQueueStatusQuery, StudentQueueStatusResult>
{
    public async Task<StudentQueueStatusResult> ExecuteAsync(GetStudentQueueStatusQuery query, CancellationToken cancellationToken)
    {
        var session = await access.ForSessionAsync(query.SessionId, query.StudentId, AuthorRole.Student, cancellationToken);
        var requests = await repository.GetSessionActivityAsync(session.Id, session.Generation, cancellationToken);
        var waitingCount = requests.Count(request => request.Status == HelpRequestStatus.Pending);
        // Estimate for a new request joining the back of the queue, even before the student submits one.
        return new StudentQueueStatusResult(waitingCount, QueueWaitEstimate.ForPosition(requests, waitingCount));
    }
}
