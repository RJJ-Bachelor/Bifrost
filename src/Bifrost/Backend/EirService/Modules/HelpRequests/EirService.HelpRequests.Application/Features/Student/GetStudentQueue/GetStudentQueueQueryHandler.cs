using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Student.GetStudentQueue;

public sealed class GetStudentQueueQueryHandler(IHelpRequestRepository repository, HelpRequestAccess access)
    : ICommandHandler<GetStudentQueueQuery, StudentQueueResult>
{
    public async Task<StudentQueueResult> ExecuteAsync(GetStudentQueueQuery query, CancellationToken cancellationToken)
    {
        var session = await access.ForSessionAsync(query.SessionId, query.StudentId, AuthorRole.Student, cancellationToken);
        var requests = await repository.GetSessionActivityAsync(session.Id, session.Generation, cancellationToken);
        var waiting = requests.Where(request => request.Status == HelpRequestStatus.Pending).ToArray();
        var own = requests.SingleOrDefault(request => request.StudentId == query.StudentId && request.IsOpen);
        var index = Array.FindIndex(waiting, request => request.Id == own?.Id);
        int? estimate = index < 0 ? null : QueueWaitEstimate.ForPosition(requests, index);
        return new StudentQueueResult(session.Id, session.Generation, waiting.Length,
            waiting.Count(request => request.AssignedTeacherId is null), own?.Id,
            index < 0 ? null : index + 1, estimate, own?.RequestNumber, own?.DisplayNumber);
    }
}
