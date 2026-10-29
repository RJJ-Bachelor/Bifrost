using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;

namespace EirService.HelpRequests.Application.Features.Common;

internal static class QueueWaitEstimate
{
    public static int? ForPosition(IReadOnlyList<HelpRequestActivity> requests, int requestsAhead)
    {
        var starts = requests.Where(request => request.StartedAt is not null &&
                request.Status is not (HelpRequestStatus.Cancelled or HelpRequestStatus.CompletedByFaq))
            .Select(request => request.StartedAt!.Value).Order().ToArray();
        if (starts.Length < 2) return null;

        // The average interval between starts measures how quickly the queue advances.
        // Simultaneous starts count too, including when several teachers help in parallel.
        var averageMinutesPerPosition = (starts[^1] - starts[0]).TotalMinutes / (starts.Length - 1);
        if (averageMinutesPerPosition <= 0) return null;

        return (int)Math.Ceiling(requestsAhead * averageMinutesPerPosition);
    }
}
