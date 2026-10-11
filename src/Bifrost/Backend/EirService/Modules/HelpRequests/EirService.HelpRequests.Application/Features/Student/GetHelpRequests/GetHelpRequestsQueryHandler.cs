using EirService.Contracts.Participants.IsSessionParticipant;
using EirService.Contracts.Sessions.GetSessionGenerations;
using EirService.HelpRequests.Application.Repositories;
using FastEndpoints;
using EirService.Contracts.Exceptions;

namespace EirService.HelpRequests.Application.Features.Student.GetHelpRequests;

public sealed class GetHelpRequestsQueryHandler(IRequestRepository repository)
    : ICommandHandler<GetHelpRequestsQuery, IReadOnlyList<HelpRequestResult>>
{
    public async Task<IReadOnlyList<HelpRequestResult>> ExecuteAsync(GetHelpRequestsQuery query, CancellationToken cancellationToken)
    {
        if (query.SessionId is { } sessionId &&
            !await new IsSessionParticipantQuery(sessionId, query.UserId).ExecuteAsync(cancellationToken))
            throw new ResourceNotFoundException();

        var requests = await repository.GetByUserIdAsync(query.UserId, cancellationToken);
        var scopedRequests = requests.Where(request => query.SessionId == null || request.SessionId == query.SessionId).ToArray();
        var ids = scopedRequests.Where(request => request.SessionId != null).Select(request => request.SessionId!.Value).Distinct().ToArray();
        var generations = ids.Length == 0 ? new Dictionary<Guid, int>() :
            await new GetSessionGenerationsQuery(ids).ExecuteAsync(cancellationToken);

        // The legacy, unscoped list must also exclude old generations of session requests.
        return scopedRequests.Where(request => request.SessionId == null ||
                generations.TryGetValue(request.SessionId.Value, out var generation) && request.Generation == generation)
            .Select(request => new HelpRequestResult(request.Id, request.UserId, request.Message, request.SessionId, request.Generation)).ToArray();
    }
}
