using EirService.Contracts.Participants.IsSessionParticipant;
using EirService.Contracts.Sessions.GetSessionGenerations;
using EirService.HelpRequests.Application.Repositories;
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
        return scoped.Where(request => generations.TryGetValue(request.SessionId, out var current) && request.Generation == current)
            .Select(request => new HelpRequestResult(request.Id, request.StudentId,
                request.Messages.OrderBy(message => message.SequenceNumber).First().Text.Value,
                request.SessionId, request.Generation, request.Status.ToString(), request.HelpType.ToString(),
                request.RequestNumber, request.DisplayNumber)).ToArray();
    }
}
