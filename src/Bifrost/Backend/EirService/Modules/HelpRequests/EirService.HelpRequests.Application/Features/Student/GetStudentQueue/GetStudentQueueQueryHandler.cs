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
        var requests = await repository.GetForSessionAsync(session.Id, session.Generation, cancellationToken);
        var waiting = requests.Where(request => request.Status == HelpRequestStatus.Pending).ToArray();
        var activeCount = requests.Count(request => request.Status == HelpRequestStatus.Active);
        var own = requests.SingleOrDefault(request => request.StudentId == query.StudentId && request.IsOpen);
        var index = Array.FindIndex(waiting, request => request.Id == own?.Id);
        var durations = requests.Where(request => request.Status == HelpRequestStatus.CompletedByPhysical &&
                request.StartedAt is not null && request.ClosedAt > request.StartedAt)
            .Select(request => (request.ClosedAt!.Value - request.StartedAt!.Value).TotalMinutes).ToArray();
        var averageMinutes = durations.Length == 0 ? 5 : durations.Average();
        int? estimate = index < 0 ? null : (int)Math.Ceiling((index + activeCount) * averageMinutes);
        return new StudentQueueResult(session.Id, session.Generation, waiting.Length,
            waiting.Count(request => request.AssignedTeacherId is null), own?.Id,
            index < 0 ? null : index + 1, estimate, own?.RequestNumber, own?.DisplayNumber);
    }
}
