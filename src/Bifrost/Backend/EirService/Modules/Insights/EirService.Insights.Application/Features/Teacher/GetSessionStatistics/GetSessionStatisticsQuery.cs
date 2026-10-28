using FastEndpoints;

namespace EirService.Insights.Application.Features.Teacher.GetSessionStatistics;

public sealed record GetSessionStatisticsQuery(Guid SessionId, string TeacherId) : ICommand<SessionStatisticsResult>;

public sealed record SessionStatisticsResult(
    int PhysicalCount, int DigitalCount, int HelpedStudentCount, double? AverageWaitMinutes);
