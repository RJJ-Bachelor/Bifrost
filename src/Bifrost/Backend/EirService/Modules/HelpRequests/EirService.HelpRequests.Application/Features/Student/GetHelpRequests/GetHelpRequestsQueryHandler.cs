using EirService.Contracts.Participants.IsSessionParticipant;
using EirService.Contracts.Sessions.GetSessionGenerations;
using EirService.Contracts.Sessions.GetSessionTeacherNames;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;
using EirService.Contracts.Exceptions;

namespace EirService.HelpRequests.Application.Features.Student.GetHelpRequests;

public sealed class GetHelpRequestsQueryHandler(IHelpRequestRepository repository)
    : ICommandHandler<GetHelpRequestsQuery, IReadOnlyList<HelpRequestResult>>
{
    public async Task<IReadOnlyList<HelpRequestResult>> ExecuteAsync(GetHelpRequestsQuery query, CancellationToken cancellationToken)
    {
        if (query.SessionId is { } sessionId &&
            !await new IsSessionParticipantQuery(sessionId, query.UserId).ExecuteAsync(cancellationToken))
            throw new ResourceNotFoundException();
        var requests = await repository.GetByUserIdAsync(query.UserId, cancellationToken);
        var scoped = requests.Where(request => query.SessionId == null || request.SessionId == query.SessionId).ToArray();
        var ids = scoped.Select(request => request.SessionId).Distinct().ToArray();
        var generations = ids.Length == 0 ? new Dictionary<Guid, int>() :
            await new GetSessionGenerationsQuery(ids).ExecuteAsync(cancellationToken);
        var currentRequests = scoped.Where(request => generations.TryGetValue(request.SessionId, out var current) && request.Generation == current)
            .OrderBy(request => request.QueuedAt).ThenBy(request => request.Id).ToArray();
        var assignedSessionIds = currentRequests.Where(request => request.AssignedTeacherId is not null)
            .Select(request => request.SessionId).Distinct().ToArray();
        var teacherNames = assignedSessionIds.Length == 0 ? new Dictionary<Guid, IReadOnlyDictionary<string, string?>>() :
            await new GetSessionTeacherNamesQuery(assignedSessionIds).ExecuteAsync(cancellationToken);
        return currentRequests
            .Select(request => new HelpRequestResult(request.Id, request.StudentId!,
                request.Messages.Single(message => message.MessageType == HelpRequestMessageType.StudentQuestion).Text.Value,
                request.SessionId, request.Generation, request.Status.ToString(), request.HelpType.ToString(),
                request.RequestNumber, request.DisplayNumber, request.QueuedAt, request.AssignedTeacherId,
                request.AssignedTeacherId is { } teacherId && teacherNames.TryGetValue(request.SessionId, out var names)
                    ? names.GetValueOrDefault(teacherId) : null)).ToArray();
    }
}
