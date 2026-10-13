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
        return new TeacherQueueResult(session.Id, session.Generation, waiting.Length,
            waiting.Select(request => request.ToResult()).ToArray(), active.Select(request => request.ToResult()).ToArray());
    }
}
