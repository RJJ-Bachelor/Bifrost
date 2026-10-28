using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;

namespace EirService.HelpRequests.Application.Features.Common;

internal static class QueueWaitEstimate
{
    public static int? ForPosition(IReadOnlyList<HelpRequestActivity> requests, int requestsAhead)
    {
        var durations = requests.Where(request => request.IsCompleted &&
                request.StartedAt is not null && request.ClosedAt > request.StartedAt)
            .Select(request => (request.ClosedAt!.Value - request.StartedAt!.Value).TotalMinutes).ToArray();
        if (durations.Length == 0) return null;
        var averageMinutes = durations.Average();
        var activeCount = requests.Count(request => request.Status == HelpRequestStatus.Active);
        return (int)Math.Ceiling((requestsAhead + activeCount) * averageMinutes);
    }
}
