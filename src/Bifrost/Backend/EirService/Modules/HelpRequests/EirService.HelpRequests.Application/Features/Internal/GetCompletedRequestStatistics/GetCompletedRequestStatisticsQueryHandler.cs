using EirService.Contracts.HelpRequests.GetCompletedRequestStatistics;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Internal.GetCompletedRequestStatistics;

public sealed class GetCompletedRequestStatisticsQueryHandler(IHelpRequestRepository repository)
    : ICommandHandler<GetCompletedRequestStatisticsQuery, CompletedRequestStatistics>
{
    public async Task<CompletedRequestStatistics> ExecuteAsync(
        GetCompletedRequestStatisticsQuery query, CancellationToken cancellationToken)
    {
        var requests = await repository.GetSessionActivityAsync(query.SessionId, query.Generation, cancellationToken);
        var completed = requests.Where(request => request.IsCompleted).ToArray();
        var averageWait = completed.Select(request => request.ClosedAt is { } closedAt
            ? (double?)(closedAt - request.QueuedAt).TotalMinutes : null).Average();
        return new CompletedRequestStatistics(
            completed.Count(request => request.HelpType == HelpType.Physical),
            completed.Count(request => request.HelpType == HelpType.Digital),
            completed.Where(request => request.StudentId is not null).Select(request => request.StudentId).Distinct().Count(),
            averageWait);
    }
}
