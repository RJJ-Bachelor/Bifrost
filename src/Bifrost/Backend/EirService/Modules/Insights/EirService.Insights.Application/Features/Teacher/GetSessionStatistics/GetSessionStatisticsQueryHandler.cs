using EirService.Contracts.Exceptions;
using EirService.Contracts.HelpRequests.GetCompletedRequestStatistics;
using EirService.Contracts.Sessions.GetHelpRequestSession;
using FastEndpoints;

namespace EirService.Insights.Application.Features.Teacher.GetSessionStatistics;

public sealed class GetSessionStatisticsQueryHandler : ICommandHandler<GetSessionStatisticsQuery, SessionStatisticsResult>
{
    public async Task<SessionStatisticsResult> ExecuteAsync(GetSessionStatisticsQuery query, CancellationToken cancellationToken)
    {
        var session = await new GetHelpRequestSessionQuery(query.SessionId).ExecuteAsync(cancellationToken)
            ?? throw new ResourceNotFoundException();
        if (!session.TeacherIds.Contains(query.TeacherId, StringComparer.Ordinal)) throw new ResourceNotFoundException();

        var statistics = await new GetCompletedRequestStatisticsQuery(session.Id, session.Generation).ExecuteAsync(cancellationToken);
        return new SessionStatisticsResult(statistics.PhysicalCount, statistics.DigitalCount,
            statistics.HelpedStudentCount, statistics.AverageWaitMinutes);
    }
}
