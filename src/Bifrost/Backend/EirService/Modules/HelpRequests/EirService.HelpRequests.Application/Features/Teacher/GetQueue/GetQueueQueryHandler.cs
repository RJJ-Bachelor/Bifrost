using EirService.Contracts.Sessions.GetSessionTeacherNames;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Teacher.GetQueue;

public sealed class GetQueueQueryHandler(IHelpRequestRepository repository, HelpRequestAccess access)
    : ICommandHandler<GetQueueQuery, TeacherQueueResult>
{
    public async Task<TeacherQueueResult> ExecuteAsync(GetQueueQuery query, CancellationToken cancellationToken)
    {
        var session = await access.ForSessionAsync(query.SessionId, query.TeacherId, AuthorRole.Teacher, cancellationToken);
        var requests = await repository.GetForSessionAsync(session.Id, session.Generation, cancellationToken);
        var waiting = requests.Where(request => request.Status == HelpRequestStatus.Pending).ToArray();
        var active = requests.Where(request => request.Status == HelpRequestStatus.Active).ToArray();
        var names = requests.Any(request => request.AssignedTeacherId is not null)
            ? await new GetSessionTeacherNamesQuery([session.Id]).ExecuteAsync(cancellationToken) : null;
        HelpRequestResult Map(HelpRequest request) => request.ToResult(request.AssignedTeacherId is { } teacherId &&
            names is not null && names.TryGetValue(session.Id, out var teachers) ? teachers.GetValueOrDefault(teacherId) : null);
        var myReservedRequestId = requests.Where(request => request.AssignedTeacherId == query.TeacherId &&
                request.Status is HelpRequestStatus.Pending or HelpRequestStatus.Active)
            .OrderByDescending(request => request.Status == HelpRequestStatus.Active)
            .ThenBy(request => request.QueuedAt).ThenBy(request => request.Id)
            .Select(request => (Guid?)request.Id).FirstOrDefault();
        return new TeacherQueueResult(session.Id, session.Generation, waiting.Length,
            waiting.Count(request => request.AssignedTeacherId is null), myReservedRequestId,
            waiting.Select(Map).ToArray(), active.Select(Map).ToArray());
    }
}
